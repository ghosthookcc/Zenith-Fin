ALTER TABLE "Transaction" DROP CONSTRAINT "Transaction_account_id_fkey";

ALTER TABLE "Account" ADD COLUMN     "transactions_synced_at" TIMESTAMPTZ(3);

ALTER TABLE "Transaction" ADD COLUMN     "amount" DECIMAL(24,8) NOT NULL,
ADD COLUMN     "bank_transaction_code" TEXT,
ADD COLUMN     "booking_date" DATE,
ADD COLUMN     "counterparty_account" TEXT,
ADD COLUMN     "counterparty_name" TEXT,
ADD COLUMN     "credit_debit_indicator" TEXT NOT NULL,
ADD COLUMN     "currency" VARCHAR(3) NOT NULL,
ADD COLUMN     "entry_reference" TEXT,
ADD COLUMN     "external_id" TEXT NOT NULL,
ADD COLUMN     "first_seen_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
ADD COLUMN     "merchant_category_code" TEXT,
ADD COLUMN     "note" TEXT,
ADD COLUMN     "occurred_on" DATE NOT NULL,
ADD COLUMN     "original_amount" DECIMAL(24,8),
ADD COLUMN     "original_currency" VARCHAR(3),
ADD COLUMN     "raw" JSONB NOT NULL,
ADD COLUMN     "reference_number" TEXT,
ADD COLUMN     "remittance_information" TEXT[] DEFAULT ARRAY[]::TEXT[],
ADD COLUMN     "status" TEXT NOT NULL,
ADD COLUMN     "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
ADD COLUMN     "value_date" DATE,
ALTER COLUMN "transaction_date" DROP NOT NULL,
ALTER COLUMN "transaction_date" DROP DEFAULT,
ALTER COLUMN "transaction_date" SET DATA TYPE DATE,
ALTER COLUMN "account_id" SET NOT NULL;

CREATE INDEX "Transaction_account_id_occurred_on_idx" ON "Transaction"("account_id", "occurred_on");

CREATE UNIQUE INDEX "Transaction_account_id_external_id_key" ON "Transaction"("account_id", "external_id");

ALTER TABLE "Transaction" ADD CONSTRAINT "Transaction_account_id_fkey" FOREIGN KEY ("account_id") REFERENCES "Account"("id") ON DELETE CASCADE ON UPDATE CASCADE;
