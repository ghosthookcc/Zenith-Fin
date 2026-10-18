ALTER TYPE "CreditCounting" ADD VALUE 'IN';
ALTER TYPE "CreditCounting" ADD VALUE 'OUT';

ALTER TABLE "CreditAccount" ADD COLUMN     "credit_limit" DECIMAL(24,8);
