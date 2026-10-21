CREATE TYPE "TransactionType" AS ENUM ('INCOME', 'EXPENSE', 'TRANSFER', 'CREDIT', 'INVESTMENT');

CREATE TYPE "AccountRole" AS ENUM ('EVERYDAY', 'SAVINGS', 'CREDIT_CARD', 'LOAN', 'INVESTMENT');

CREATE TYPE "CreditMode" AS ENUM ('MIRROR', 'CARD');

ALTER TABLE "Account" ADD COLUMN     "bank_account_type" TEXT,
ADD COLUMN     "bank_product" TEXT,
ADD COLUMN     "role" "AccountRole";

ALTER TABLE "CreditAccount" ADD COLUMN     "mode" "CreditMode" NOT NULL DEFAULT 'MIRROR';

ALTER TABLE "Transaction" ADD COLUMN     "type_override" "TransactionType";

