using GoCardless.Resources;
using System.Security.Principal;
using static ZenithFin.EnableBanking.Response;

namespace ZenithFin.EnableBanking
{
    public static class Response
    {
        internal sealed record AccountId(string iban,
                                         OtherAccountId? other);
        internal sealed record OtherAccountId(string identification,
                                              string schemeName,
                                              string? issuer);
        internal sealed record AccountServicer(string bicFi,
                                               ClearingSystemMemberId clearingSystemMemberId,
                                               string? name);
        internal sealed record ClearingSystemMemberId(string clearingSystemId,
                                                      string memberId);
        internal sealed record CreditLimit(string currency,
                                           string amount);
        internal sealed record PostalAddress(string? addressType,
                                             string? department,
                                             string? subDepartment,
                                             string? streetName,
                                             string? buildingNumber,
                                             string? postCode,
                                             string? townName,
                                             string? countrySubDivision,
                                             string? country,
                                             IReadOnlyList<string>? addressLine);
        internal sealed record Aspsp(string name,
                                     string country);
        internal sealed record Access(DateTime validUntil);
        internal sealed record AccountData(AccountId accountId,
                                           IReadOnlyList<OtherAccountId> allAccountIds,
                                           AccountServicer accountServicer,

                                           string? name,
                                           string? details,
                                           string? usage,

                                           string cashAccountType,
                                           string product,
                                           string currency,
                                           string psuStatus,

                                           CreditLimit? creditLimit,
                                           bool? legalAge,
                                           PostalAddress? postalAddress,

                                           Guid uid,
                                           string identificationHash,
                                           IReadOnlyList<string> identificationHashes);

        internal record Base();
        internal sealed record Application(string name,
                                           string? description,
                                           string kid,
                                           string environment,
                                           IReadOnlyList<string> redirectUrls,
                                           bool active,
                                           IReadOnlyList<string> countries,
                                           IReadOnlyList<string> services) : Base;
        internal sealed record Authenticate(string url,
                                            string authenticationId,
                                            string psuIdHash) : Base;
        internal sealed record Sessions(string sessionId,
                                        IReadOnlyList<AccountData> accounts,
                                        Aspsp aspsp,
                                        string psuType,
                                        Access access);
        internal sealed record Balance(string? name,
                                       BalanceAmount balanceAmount,
                                       string balanceType,
                                       DateTime? lastChangeDateTime,
                                       DateOnly? referenceDate,
                                       string? lastCommittedTransaction);
        internal sealed record BalanceAmount(string currency,
                                             string amount);
        internal sealed record AccountsBalances(IReadOnlyList<Balance> balances);
    }

}
