using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using ZenithFin.Api.Auth;
using ZenithFin.Api.Models.Dtos;
using ZenithFin.Api.Models.Entities;
using ZenithFin.EnableBanking;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Services;
using ZenithFin.Utility;

namespace ZenithFin.Api.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/aspsp")]
    public class Banking : ControllerBase
    {
        private readonly EnableBankingWorkspace _workspace;

        private readonly UserService _userService;
        private readonly BankingService _bankingService;
        private readonly TransactionService _transactionService;
        private readonly CorrectionService _correctionService;
        private readonly AccountAliasService _accountAliasService;
        private readonly RecurringService _recurringService;

        private readonly JwtAuthenticator _jwtAuthenticator;

        private const int MaxTransactionRangeDays = 400;

        [HttpGet]
        [ResourceGuard]
        [Route("all")]
        public IActionResult ListAspsps()
        {
            AspspDto.AllAspsps? aspsps = _workspace.config!
                                                   .RootElement
                                                   .GetProperty("EnableBanking")
                                                   .Deserialize<AspspDto.AllAspsps>();

            AspspDto.AspspsResponse response = new();
            response.Message = "Retrived ASPSPS";
            response.Success = true;
            response.Code = StatusCodes.Status200OK;

            if (aspsps != null) response.Aspsps = aspsps;

            return Ok(response);
        }

        [HttpGet]
        [ResourceGuard]
        [Route("all/inactive")]
        public async Task<IActionResult> ListInactiveAspsps()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;
            if (string.IsNullOrEmpty(sessionId))
                return Unauthorized("No session ID found");

            AspspDto.AllAspsps? aspsps = _workspace.config!
                                       .RootElement
                                       .GetProperty("EnableBanking")
                                       .Deserialize<AspspDto.AllAspsps>();

            AspspDto.AspspsResponse response = new();
            response.Message = "Retrived ASPSPS";
            response.Success = true;
            response.Code = StatusCodes.Status200OK;

            AspspDto.AllAspsps? inactiveAspsps = await _bankingService.GetInactiveAspsps(sessionId,
                                                                                         aspsps);
            if (inactiveAspsps != null) response.Aspsps = inactiveAspsps;

            return Ok(response);
        }

        [HttpPost]
        [ResourceGuard]
        [Route("auth/callback")]
        public async Task<IActionResult> HandleAuthCallback([FromBody] AspspDto.AspspAuthenticationCallbackRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;
            if (string.IsNullOrEmpty(sessionId))
                return Unauthorized("No session ID found");
            Console.WriteLine("Code: " + request.Code);

            AspspAuthenticationCallbackAttempt attempt = await _workspace.Authenticator.AuthenticateCallback(request.Code);
            if (attempt.success && attempt.sessionId != null && attempt.expiresAt != null)
            {
                await _bankingService.StartAspspSessionAsync(attempt.sessionId, attempt.expiresAt.Value,
                                                             sessionId,
                                                             request.State,
                                                             attempt.accounts ?? Array.Empty<EnableBankingEntities.AccountData>());
            }

            return Ok(new
            {
                ExpiresAt = attempt.expiresAt
            });
        }

        [HttpGet]
        [ResourceGuard]
        [Route("accounts/transactions")]
        public async Task<IActionResult> GetTransactions([FromQuery] DateOnly? from,
                                                         [FromQuery] DateOnly? to)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            DateOnly fromDate = from ?? new DateOnly(today.Year, today.Month, 1);
            DateOnly toDate = to ?? fromDate.AddMonths(1).AddDays(-1);

            if (toDate < fromDate)
                return BadRequest(new { message = "'to' must not be before 'from'", success = false });

            if (toDate.DayNumber - fromDate.DayNumber > MaxTransactionRangeDays)
                return BadRequest(new { message = $"The date range can span at most {MaxTransactionRangeDays} days", success = false });

            TransactionDto.TransactionsResponse? transactions = await _transactionService.GetTransactionsAsync(sessionId, fromDate, toDate);

            if (transactions == null) return Unauthorized("No user found for session");

            return Ok(transactions);
        }

        [HttpPost]
        [ResourceGuard]
        [Route("accounts/transactions/sync")]
        public async Task<IActionResult> SyncTransactions([FromQuery] bool force = false)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            TransactionDto.SyncResponse? result = await _transactionService.SyncTransactionsAsync(sessionId, force);

            if (result == null) return Unauthorized("No user found for session");

            return Ok(result);
        }

        [HttpPut]
        [ResourceGuard]
        [Route("accounts/transactions/{id:long}/tag")]
        public async Task<IActionResult> CorrectTransactionTag(long id,
                                                               [FromBody] TransactionDto.CorrectTagRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            CorrectionResult result = await _correctionService.CorrectAsync(sessionId, id, request);

            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return Ok(result.Response);
        }

        [HttpPut]
        [ResourceGuard]
        [Route("accounts/transactions/{id:long}/recurring")]
        public async Task<IActionResult> SetRecurring(long id,
                                                      [FromBody] TransactionDto.SetRecurringRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            RecurringResult result = await _recurringService.SetAsync(sessionId, id, request);

            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return Ok(new { recurring = result.Recurring });
        }

        [HttpPut]
        [ResourceGuard]
        [Route("accounts/transactions/{id:long}/type")]
        public async Task<IActionResult> SetType(long id,
                                                 [FromBody] TransactionDto.SetTypeRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            TypeResult result = await _transactionService.SetTypeAsync(sessionId, id, request);

            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return Ok(new { type = result.Type });
        }

        [HttpPut]
        [ResourceGuard]
        [Route("accounts/transactions/{id:long}/counting")]
        public async Task<IActionResult> SetCounting(long id,
                                                     [FromBody] TransactionDto.SetCountingRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            CountingResult result = await _transactionService.SetCountingAsync(sessionId, id, request);

            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return Ok(new { counting = result.Counting });
        }

        [HttpGet]
        [ResourceGuard]
        [Route("connections")]
        public async Task<IActionResult> GetConnections()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            AspspDto.BankConnection[]? connections = await _bankingService.GetConnectionsAsync(sessionId);

            if (connections == null) return Unauthorized("No user found for session");

            return Ok(connections);
        }

        [HttpDelete]
        [ResourceGuard]
        [Route("connections/{id:long}")]
        public async Task<IActionResult> RemoveConnection(long id)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            (int statusCode, string? message, AspspDto.RemovedBankConnection? removed) = await _bankingService.RemoveConnectionAsync(sessionId, id);

            if (statusCode != StatusCodes.Status200OK)
                return StatusCode(statusCode, new { message, success = false });

            return Ok(removed);
        }

        [HttpPut]
        [ResourceGuard]
        [Route("accounts/{id:long}/alias")]
        public async Task<IActionResult> RenameAccount(long id,
                                                       [FromBody] AccountDto.RenameAccountRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            AccountAliasResult result = await _accountAliasService.RenameAsync(sessionId, id, request);

            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return Ok(result.AccountAlias);
        }

        [HttpPut]
        [ResourceGuard]
        [Route("accounts/{id:long}/role")]
        public async Task<IActionResult> SetAccountRole(long id,
                                                        [FromBody] AccountDto.SetAccountRoleRequest request)
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");

            AccountAliasResult result = await _accountAliasService.SetRoleAsync(sessionId, id, request);

            if (result.StatusCode != StatusCodes.Status200OK)
                return StatusCode(result.StatusCode, new { message = result.Message, success = false });

            return Ok(new { success = true });
        }

        [HttpGet]
        [ResourceGuard]
        [Route("accounts/balances")]
        public async Task<IActionResult> GetAccountsBalances()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");
            
            AccountDto.Balance[] balances = await _bankingService.GetAccountsBalancesAsync(sessionId);
            
            return Ok(balances);
        }
        
        [HttpGet]
        [ResourceGuard]
        [Route("accounts/balances/expanded")]
        public async Task<IActionResult> GetAccountsBalancesExpanded()
        {
            string? sessionId = HttpContext.Items["SessionId"] as string;

            if (string.IsNullOrEmpty(sessionId)) return Unauthorized("No session ID found");
            
            AccountDto.Balance[] balancesExpanded = await _bankingService.GetAccountsBalancesAsync(sessionId);
            
            return Ok(balancesExpanded);
        }
        
        public Banking(EnableBankingWorkspace workspace,
                       UserService userService,
                       BankingService bankingService,
                       TransactionService transactionService,
                       CorrectionService correctionService,
                       AccountAliasService accountAliasService,
                       RecurringService recurringService,
                       JwtAuthenticator jwtAuthenticator)
        {
            _workspace = workspace;
            _userService = userService;
            _bankingService = bankingService;
            _transactionService = transactionService;
            _correctionService = correctionService;
            _accountAliasService = accountAliasService;
            _recurringService = recurringService;
            _jwtAuthenticator = jwtAuthenticator;
        }
    }
}
