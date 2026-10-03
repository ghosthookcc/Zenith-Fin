/*
  Warnings:

  - A unique constraint covering the columns `[user_id,aspsp_name,aspsp_country]` on the table `BankConnection` will be added. If there are existing duplicate values, this will fail.

*/
-- DropIndex
DROP INDEX "BankConnection_aspsp_name_aspsp_country_key";

-- AlterTable
ALTER TABLE "Account" ADD COLUMN     "balances_fetched_at" TIMESTAMPTZ(3);

-- CreateTable
CREATE TABLE "Balance" (
    "id" BIGSERIAL NOT NULL,
    "account_id" BIGINT NOT NULL,
    "amount" DECIMAL(24,8) NOT NULL,
    "currency" VARCHAR(3) NOT NULL,
    "balance_type" TEXT NOT NULL,
    "name" TEXT,
    "last_committed_transaction" TEXT,
    "last_change_date_time" TIMESTAMPTZ(3),
    "reference_date" DATE,
    "fetched_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "Balance_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "Balance_account_id_balance_type_currency_key" ON "Balance"("account_id", "balance_type", "currency");

-- CreateIndex
CREATE UNIQUE INDEX "BankConnection_user_id_aspsp_name_aspsp_country_key" ON "BankConnection"("user_id", "aspsp_name", "aspsp_country");

-- AddForeignKey
ALTER TABLE "Balance" ADD CONSTRAINT "Balance_account_id_fkey" FOREIGN KEY ("account_id") REFERENCES "Account"("id") ON DELETE RESTRICT ON UPDATE CASCADE;
