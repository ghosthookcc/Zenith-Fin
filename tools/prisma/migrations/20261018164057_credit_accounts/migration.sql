CREATE TYPE "CreditCounting" AS ENUM ('NEUTRAL', 'FLOW');

CREATE TABLE "CreditAccount" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "account_id" BIGINT NOT NULL,
    "name" TEXT NOT NULL,
    "counting" "CreditCounting" NOT NULL DEFAULT 'NEUTRAL',
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "CreditAccount_pkey" PRIMARY KEY ("id")
);

CREATE UNIQUE INDEX "CreditAccount_account_id_key" ON "CreditAccount"("account_id");

CREATE INDEX "CreditAccount_user_id_idx" ON "CreditAccount"("user_id");

CREATE UNIQUE INDEX "CreditAccount_user_id_name_key" ON "CreditAccount"("user_id", "name");

ALTER TABLE "CreditAccount" ADD CONSTRAINT "CreditAccount_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "CreditAccount" ADD CONSTRAINT "CreditAccount_account_id_fkey" FOREIGN KEY ("account_id") REFERENCES "Account"("id") ON DELETE CASCADE ON UPDATE CASCADE;
