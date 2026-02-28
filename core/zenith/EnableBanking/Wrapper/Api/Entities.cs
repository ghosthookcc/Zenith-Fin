namespace ZenithFin.EnableBanking
{
    public static class EnableBankingEntities
    {
        public record Base();

        public sealed record AccountId(string iban,
                                         OtherAccountId? other);
        public sealed record OtherAccountId(string identification,
                                              string schemeName,
                                              string? issuer);
        public sealed record AccountServicer(string bicFi,
                                               ClearingSystemMemberId clearingSystemMemberId,
                                               string? name);
        public sealed record ClearingSystemMemberId(string clearingSystemId,
                                                      string memberId);
        public sealed record CreditLimit(string currency,
                                           string amount);
        public sealed record PostalAddress(string? addressType,
                                             string? department,
                                             string? subDepartment,
                                             string? streetName,
                                             string? buildingNumber,
                                             string? postCode,
                                             string? townName,
                                             string? countrySubDivision,
                                             string? country,
                                             IReadOnlyList<string>? addressLine);
        public sealed record Aspsp(string name,
                                   string country);
        public sealed record Access(DateTimeOffset validUntil);
        public sealed record AccountData(AccountId accountId,
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

        public sealed record Balance(string? name,
                                     BalanceAmount balanceAmount,
                                     string balanceType,
                                     DateTime? lastChangeDateTime,
                                     DateOnly? referenceDate,
                                     string? lastCommittedTransaction);
        public sealed record BalanceAmount(string currency,
                                           string amount);

        public sealed record Credential(string Description,
                                        string Name,
                                        bool Required,
                                        string Template,
                                        string Title);

        public sealed record RemittanceInformationLine(int maxLength,
                                                       int MinLength,
                                                       string Pattern);

        public sealed record Payment(IReadOnlyList<string> allowedAuthMethods,
                                     IReadOnlyList<string> chargeBearerValues,
                                     IReadOnlyList<string> creditorAccountSchemas,
                                     bool creditorAgentBicFiRequired,
                                     bool creditorAgentClearingSystemMemberIdRequired,
                                     bool creditorCountryRequired,
                                     bool creditorNameRequired,
                                     bool creditorPostalAddressRequired,
                                     IReadOnlyList<string> currencies,
                                     bool debtorAccountRequired,
                                     IReadOnlyList<string> debtorAccountSchemas,
                                     bool debtorContactEmailRequired,
                                     bool debtorContactPhoneRequired,
                                     bool debtorCurrencyRequired,
                                     int maxTransactions,
                                     string paymentType,
                                     IReadOnlyList<string> priorityCodes,
                                     string psuType,
                                     IReadOnlyList<string> referenceNumberSchemas,
                                     bool referenceNumberSupported,
                                     bool regulatoryReportingCodeRequired,
                                     IReadOnlyList<RemittanceInformationLine> remittanceInformationLines,
                                     bool remittanceInformationRequired,
                                     int requestedExecutionDateMaxPeriod,
                                     bool requestedExecutionDateSupported);

        public sealed record AuthMethod(string approach,
                                        IReadOnlyList<Credential> credentials,
                                        bool hiddenMethod,
                                        string name,
                                        string psuType);

        public sealed record AspspDetailed(IReadOnlyList<AuthMethod> authMethods,
                                           bool beta,
                                           string bic,
                                           string country,
                                           string logo,
                                           int maximumConsentValidity,
                                           string name,
                                           IReadOnlyList<Payment> payments,
                                           IReadOnlyList<string> psuTypes,
                                           IReadOnlyList<string> requiredPsuHeaders);
    }
}
