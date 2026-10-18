namespace ZenithFin.Categorization
{
    public static class CategoryRules
    {
        public const string Groceries = "groceries";
        public const string Dining = "dining";
        public const string Transport = "transport";
        public const string Housing = "housing";
        public const string Bills = "bills";
        public const string Shopping = "shopping";
        public const string Entertainment = "entertainment";
        public const string Health = "health";
        public const string Travel = "travel";
        public const string Savings = "savings";
        public const string Fees = "fees";
        public const string Income = "income";
        public const string Transfers = "transfers";
        public const string Other = "other";

        public static readonly KeywordPattern[] OwnTransferCodes = Patterns("OVERFORING EGNA");

        public static readonly KeywordPattern[] IncomeKeywords = Patterns(
            "LON", "LONEUTBETALNING", "SALARY", "STUDIESTOD", "CSN", "CENTRALA STUDIESTODSNAMNDEN", "RANTA",
            "SKATTEVERKET", "SKATTEATERBARING", "PENSION*", "FORSAKRINGSKASSAN", "UTDELNING", "AKTIEUTDELNING",
            "BARNBIDRAG", "ARVODE", "ELSTOD", "SKATTEATER*");

        public static readonly (string Key, KeywordPattern[] Patterns)[] Keywords =
        {
            (Housing, Patterns(
                "HSB*", "RIKSBYGGEN", "BRF", "BOSTADSRATTSFORENING*", "HYRA", "HYRESAVI", "STADSHYPOTEK", "SBAB", "BOLAN*",
                "BOSTADSFORMEDLINGEN", "HEIMSTADEN", "AKELIUS", "WALLENSTAM", "FAMILJEBOSTADER", "SVENSKA BOSTADER",
                "STOCKHOLMSHEM", "FASTIGHETER")),
            (Bills, Patterns(
                "VATTENFALL", "BOO ENERGI", "ENERGI", "ELLEVIO", "E ON", "EON", "FORTUM", "TIBBER", "GREENELY", "GODEL",
                "TELIA", "TELE2", "TELENOR", "HI3G", "TRE SVERIGE", "COMVIQ", "HALLON", "VIMLA", "BAHNHOF", "BREDBAND*",
                "OWNIT", "COMHEM", "COM HEM", "IF SKADE*", "IF FORSAKRING", "TRYGG HANSA", "FOLKSAM", "LANSFORSAKRINGAR",
                "GJENSIDIGE", "ICA FORSAKRING", "ICA BANKEN", "SAAK", "AKASSA", "A KASSA", "UNIONEN", "CSN",
                "CENTRALA STUDIESTODSNAMNDEN", "AMEX", "AMERICAN EXPRESS", "RESURS BANK", "SVEA")),
            (Health, Patterns(
                "APOTEK*", "KRONANS", "HJARTAT", "LLOYDS", "SYNSAM", "SPECSAVERS", "SMARTEYES", "VARDCENTRAL*",
                "TANDLAKAR*", "TANDVARD*", "FOLKTANDVARDEN", "KRY", "MIN DOKTOR", "SATS", "FRISKIS*", "NORDIC WELLNESS",
                "ACTIC", "FITNESS24SEVEN", "GYM*", "*SJUKHUS*", "*BADET", "SIMHALL*", "BADHUS*")),
            (Transport, Patterns(
                "STORSTOCKHOLMS LO*", "SL", "SJ", "VASTTRAFIK", "SKANETRAFIKEN", "OSTGOTATRAFIKEN", "EASYPARK", "PARKSTER",
                "APCOA", "AIMO", "MOOVY", "UBER", "BOLT", "TAXI*", "CABONLINE", "CIRCLE K", "PREEM", "OKQ8", "ST1", "INGO",
                "SHELL", "VOI", "TIER", "LIME", "FLYGBUSSARNA", "ARLANDA EXPRESS", "TRANGSELSKATT", "BILPROVNING*",
                "BESIKTNING*", "PARKERING*")),
            (Travel, Patterns(
                "RYANAIR", "SAS", "NORWEGIAN", "LUFTHANSA", "KLM", "FINNAIR", "WIZZ*", "EASYJET", "VUELING", "BOOKING COM",
                "AIRBNB", "HOTELS COM", "EXPEDIA", "SCANDIC", "STRAWBERRY", "NORDIC CHOICE", "BEST WESTERN", "HILTON",
                "MARRIOTT", "RADISSON", "HOTEL*", "HOSTEL*", "SWEDAVIA", "TUI", "APOLLO", "VING", "KONTANTUTTAG UTL*")),
            (Dining, Patterns(
                "RESTAURANG*", "RESTAURANT*", "CAFE*", "KAFE*", "COFFEE", "ESPRESSO HOUSE", "WAYNES", "WAYNE S", "STARBUCKS",
                "JOE AND THE JUICE", "JOE THE JUICE", "KFC", "NAPOLETANA", "FOODTR*", "BAGERI*", "KONDITORI*", "BAR", "PUB", "BISTRO*", "KROG*", "GRILL*", "BARBECUE",
                "BURGER*", "MAX BURGERS", "MAX HAMBURGARE", "MCDONALD*", "MC DONALD*", "SUBWAY", "SIBYLLA", "PIZZ*",
                "SUSHI*", "KEBAB*", "DONER", "FALAFEL", "THAI", "RAMEN", "TACO*", "WOK*", "NOODLE*", "TRATTORIA", "OSTERIA",
                "TAVERNA", "OLEARYS", "O LEARYS", "BRODERNAS", "UBER EATS", "BOLT FOOD", "FOODORA", "WOLT", "QOPLA",
                "BAR OCH KOK")),
            (Groceries, Patterns(
                "ICA", "COOP", "HEMKOP", "WILLYS", "LIDL", "CITY GROSS", "NETTO", "TEMPO", "MATHEM", "MATSMART", "AXFOOD",
                "GOOD2GO*", "CONVINI", "PRESSBYRAN", "7 ELEVEN", "SEVEN ELEVEN", "SPEL OCH TO*", "SYSTEMBOLAGET", "LIVS",
                "MATBUTIK*", "SUPERMARKET*", "SUPERMERCADO*", "MERCADONA", "CARREFOUR", "BULLANDO", "CARAMELLA", "SHOP AND GO", "KIOSK*")),
            (Shopping, Patterns(
                "WEBHALLEN", "WH", "BYGGMAX", "BEIJER", "VEDUM", "BAUHAUS", "HORNBACH", "JULA", "BILTEMA", "CLAS OHLSON",
                "IKEA", "JYSK", "RUSTA", "ELGIGANTEN", "MEDIAMARKT", "MEDIA MARKT", "NETONNET", "KJELL", "INET", "ELBUTIK*",
                "KOMPLETT", "PROSHOP", "ZALANDO", "H M", "HM", "LINDEX", "KAPPAHL", "AHLENS", "DESIGNTORGET", "STADIUM",
                "XXL", "INTERSPORT", "AMAZON*", "CDON", "ALIEXPRESS", "TEMU", "SHEIN", "ETSY", "EBAY", "TRADERA",
                "APPLE STORE", "KLARNA", "WALLEY", "ADLIBRIS", "BOKUS", "STORE", "NORMAL", "BLOMMOR", "BLOMSTER*", "SP")),
            (Entertainment, Patterns(
                "STEAM*", "G2A*", "LOOTBAR", "GOOGLE", "GOOGLE PLAY", "PLAYSTATION", "XBOX", "NINTENDO", "EPIC GAMES", "BLIZZARD",
                "SPOTIFY", "NETFLIX", "HBO*", "HBO MAX", "DISNEY*", "VIAPLAY", "YOUTUBE*", "TWITCH", "AUDIBLE",
                "STORYTEL", "BOOKBEAT", "NEXTORY", "ITUNES", "APPLE COM BILL", "FILMSTADEN", "SF BIO", "BIOGRAF*",
                "TICKETMASTER", "LIVE NATION", "TIXLY", "GRONA L*", "LISEBERG", "CHINATEATERN", "TEATER*", "BILJARD",
                "BOWLING", "MUSEUM", "SKANSEN", "PADEL*", "KONSERT*")),
            (Savings, Patterns(
                "AVANZA", "NORDNET", "LYSA", "SAVR", "SPILTAN", "PENSIONSSPAR*", "FONDSPAR*")),
            (Fees, Patterns(
                "BASERBJUDANDE", "VARDAGSPAKET", "BASAVGIFT*", "ARSAVGIFT", "MANADSAVGIFT", "KORTAVGIFT", "AVGIFT",
                "PAMINNELSEAVGIFT", "SKATT", "SKATTEVERKET", "KRONOFOGDEN", "RANTA", "FELPARKERING*", "PARKERINGSBOT", "POLISEN", "UC AB")),
        };

        private static KeywordPattern[] Patterns(params string[] patterns)
        {
            return patterns.Select(pattern => new KeywordPattern(pattern)).ToArray();
        }
    }
}
