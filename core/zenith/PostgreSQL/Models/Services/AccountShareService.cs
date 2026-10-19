using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.DataProtection;

using ZenithFin.Api.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Dtos;
using ZenithFin.PostgreSQL.Models.Entities;
using ZenithFin.PostgreSQL.Models.Repositories;
using ZenithFin.Utility;

namespace ZenithFin.PostgreSQL.Models.Services
{
    public sealed record AccountShareResult(int StatusCode,
                                            string? Message,
                                            AccountShareDto.AccountShare? Share = null);

    public sealed record PersonalNumberResult(int StatusCode,
                                              string? Message,
                                              string? PersonalNumber = null);

    public sealed class AccountShareService
    {
        private sealed record Person(string FirstName,
                                     string LastName,
                                     string? PersonalNumber);

        private readonly AccountShareRepository _accountShareRepository;
        private readonly UserRepository _userRepository;
        private readonly Protector _protector;

        private const int MaxNameLength = 50;

        private const string PersonalNumberShape = "Personal numbers look like YYYYMMDD-XXXX";

        private static readonly Regex PersonalNumberFormat = new(@"^(\d{2})?(\d{2})(\d{2})(\d{2})([-+]?)(\d{4})$", RegexOptions.Compiled);

        private static readonly AccountShareResult Unauthorized = new(StatusCodes.Status401Unauthorized, "No user found for session");

        public AccountShareService(AccountShareRepository accountShareRepository,
                                   UserRepository userRepository,
                                   IDataProtectionProvider provider)
        {
            _accountShareRepository = accountShareRepository;
            _userRepository = userRepository;

            _protector = new Protector(provider, ProtectorPurpose.PersonalNumber);
        }

        public async Task<AccountShareDto.AccountSharesResponse?> GetSharesAsync(string sessionId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return null;

            AccountShareRow[] shares = await _accountShareRepository.GetSharesForUserAsync(userId.Value);

            return new AccountShareDto.AccountSharesResponse
            {
                Shares = shares.Select(ToDto).ToArray()
            };
        }

        public async Task<AccountShareResult> CreateAsync(string sessionId,
                                                          AccountShareDto.SaveAccountShareRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            if (request.AccountId == null) return BadRequest("Pick the account you share");

            (Person? person, AccountShareResult? invalid) = ValidatePerson(request);

            if (person == null) return invalid!;

            AccountShareRow[] existing = await _accountShareRepository.GetSharesForUserAsync(userId.Value);

            AccountShareRow? duplicate = FindDuplicate(existing, request.AccountId.Value, person, null);

            if (duplicate != null) return Duplicate(duplicate);

            string? encrypted = person.PersonalNumber == null ? null : _protector.Encrypt(person.PersonalNumber);

            long? id = await _accountShareRepository.InsertShareAsync(userId.Value,
                                                                      request.AccountId.Value,
                                                                      person.FirstName,
                                                                      person.LastName,
                                                                      encrypted);

            if (id == null) return new AccountShareResult(StatusCodes.Status404NotFound, "Account not found");

            return Saved(new AccountShareRow
            {
                Id = id.Value,
                AccountId = request.AccountId.Value,
                FirstName = person.FirstName,
                LastName = person.LastName,
                PersonalNumberEncrypted = encrypted
            });
        }

        public async Task<AccountShareResult> UpdateAsync(string sessionId,
                                                          long shareId,
                                                          AccountShareDto.SaveAccountShareRequest request)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            AccountShareRow[] existing = await _accountShareRepository.GetSharesForUserAsync(userId.Value);
            AccountShareRow? current = existing.FirstOrDefault(share => share.Id == shareId);

            if (current == null) return NotFound();

            (Person? person, AccountShareResult? invalid) = ValidatePerson(request);

            if (person == null) return invalid!;

            AccountShareRow? duplicate = FindDuplicate(existing, current.AccountId, person, shareId);

            if (duplicate != null) return Duplicate(duplicate);

            bool replace = person.PersonalNumber != null || request.RemovePersonalNumber;
            string? encrypted = person.PersonalNumber == null ? null : _protector.Encrypt(person.PersonalNumber);

            if (!await _accountShareRepository.UpdateShareAsync(userId.Value,
                                                                shareId,
                                                                person.FirstName,
                                                                person.LastName,
                                                                replace,
                                                                encrypted))
                return NotFound();

            return Saved(new AccountShareRow
            {
                Id = shareId,
                AccountId = current.AccountId,
                FirstName = person.FirstName,
                LastName = person.LastName,
                PersonalNumberEncrypted = replace ? encrypted : current.PersonalNumberEncrypted
            });
        }

        public async Task<AccountShareResult> DeleteAsync(string sessionId,
                                                          long shareId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return Unauthorized;

            return await _accountShareRepository.DeleteShareAsync(userId.Value, shareId)
                ? new AccountShareResult(StatusCodes.Status200OK, null)
                : NotFound();
        }

        public async Task<PersonalNumberResult> RevealPersonalNumberAsync(string sessionId,
                                                                          long shareId)
        {
            long? userId = await GetUserIdAsync(sessionId);

            if (userId == null) return new PersonalNumberResult(StatusCodes.Status401Unauthorized, "No user found for session");

            AccountShareRow[] shares = await _accountShareRepository.GetSharesForUserAsync(userId.Value);
            AccountShareRow? share = shares.FirstOrDefault(row => row.Id == shareId);

            if (share == null) return new PersonalNumberResult(StatusCodes.Status404NotFound, "Person not found");

            if (share.PersonalNumberEncrypted == null)
                return new PersonalNumberResult(StatusCodes.Status404NotFound, "No personal number is saved for this person");

            try
            {
                return new PersonalNumberResult(StatusCodes.Status200OK, null, _protector.Decrypt(share.PersonalNumberEncrypted));
            }
            catch (CryptographicException)
            {
                return new PersonalNumberResult(StatusCodes.Status500InternalServerError,
                                                "The personal number can't be read any more. Remove it and add it again.");
            }
        }

        private static (Person? Person, AccountShareResult? Invalid) ValidatePerson(AccountShareDto.SaveAccountShareRequest request)
        {
            string? firstName = TransactionText.Clean(request.FirstName);

            if (firstName == null) return (null, BadRequest("Add the person's first name"));

            string? lastName = TransactionText.Clean(request.LastName);

            if (lastName == null) return (null, BadRequest("Add the person's last name"));

            if (firstName.Length > MaxNameLength || lastName.Length > MaxNameLength)
                return (null, BadRequest($"Names can be at most {MaxNameLength} characters"));

            string personalNumber = new string((request.PersonalNumber ?? "").Where(character => !char.IsWhiteSpace(character)).ToArray());

            if (personalNumber.Length == 0) return (new Person(firstName, lastName, null), null);

            string? normalized = NormalizePersonalNumber(personalNumber, DateTime.Today, out string problem);

            return normalized == null
                ? (null, BadRequest(problem))
                : (new Person(firstName, lastName, normalized), null);
        }

        public static string? NormalizePersonalNumber(string value,
                                                      DateTime today,
                                                      out string problem)
        {
            problem = PersonalNumberShape;

            Match match = PersonalNumberFormat.Match(value);

            if (!match.Success) return null;

            int shortYear = int.Parse(match.Groups[2].Value);
            int month = int.Parse(match.Groups[3].Value);
            int dayDigits = int.Parse(match.Groups[4].Value);
            int day = dayDigits > 60 ? dayDigits - 60 : dayDigits;
            string serial = match.Groups[6].Value;

            int year;

            if (match.Groups[1].Success)
            {
                year = int.Parse(match.Groups[1].Value) * 100 + shortYear;
            }
            else
            {
                year = today.Year / 100 * 100 + shortYear;

                if (year > today.Year) year -= 100;
                if (match.Groups[5].Value == "+") year -= 100;
            }

            if (month is < 1 or > 12 || day < 1 || year < 1850 || day > DateTime.DaysInMonth(year, month)) return null;
            if (new DateTime(year, month, day) > today) return null;

            if (!PassesLuhn($"{shortYear:D2}{month:D2}{dayDigits:D2}{serial}"))
            {
                problem = "That personal number doesn't add up, check the digits";
                return null;
            }

            return $"{year:D4}{month:D2}{dayDigits:D2}-{serial}";
        }

        private static bool PassesLuhn(string digits)
        {
            int sum = 0;

            for (int index = 0; index < digits.Length; index++)
            {
                int digit = digits[index] - '0';

                if (index % 2 == 0)
                {
                    digit *= 2;

                    if (digit > 9) digit -= 9;
                }

                sum += digit;
            }

            return sum % 10 == 0;
        }

        private static AccountShareRow? FindDuplicate(IEnumerable<AccountShareRow> existing,
                                                      long accountId,
                                                      Person person,
                                                      long? editedId)
        {
            return existing.FirstOrDefault(share => share.Id != editedId &&
                                                    share.AccountId == accountId &&
                                                    string.Equals(share.FirstName, person.FirstName, StringComparison.OrdinalIgnoreCase) &&
                                                    string.Equals(share.LastName, person.LastName, StringComparison.OrdinalIgnoreCase));
        }

        private static AccountShareDto.AccountShare ToDto(AccountShareRow row)
        {
            return new AccountShareDto.AccountShare
            {
                Id = row.Id,
                AccountId = row.AccountId,
                FirstName = row.FirstName,
                LastName = row.LastName,
                HasPersonalNumber = row.PersonalNumberEncrypted != null
            };
        }

        private static AccountShareResult Saved(AccountShareRow row)
        {
            return new AccountShareResult(StatusCodes.Status200OK, null, ToDto(row));
        }

        private static AccountShareResult BadRequest(string message)
        {
            return new AccountShareResult(StatusCodes.Status400BadRequest, message);
        }

        private static AccountShareResult NotFound()
        {
            return new AccountShareResult(StatusCodes.Status404NotFound, "Person not found");
        }

        private static AccountShareResult Duplicate(AccountShareRow existing)
        {
            return new AccountShareResult(StatusCodes.Status409Conflict, $"{existing.FirstName} {existing.LastName} is already on this account");
        }

        private async Task<long?> GetUserIdAsync(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out Guid activeSessionId)) return null;

            UserEssentials? essentials = await _userRepository.GetUserIdBySessionId(activeSessionId);

            return essentials?.UserId;
        }
    }
}
