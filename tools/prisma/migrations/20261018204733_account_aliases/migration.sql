CREATE TABLE "AccountAlias" (
    "account_id" BIGINT NOT NULL,
    "bank_name_alias" TEXT,

    CONSTRAINT "AccountAlias_pkey" PRIMARY KEY ("account_id")
);

ALTER TABLE "AccountAlias" ADD CONSTRAINT "AccountAlias_account_id_fkey" FOREIGN KEY ("account_id") REFERENCES "Account"("id") ON DELETE CASCADE ON UPDATE CASCADE;
