/*
  Warnings:

  - You are about to drop the column `active_session_id` on the `BankConnection` table. All the data in the column will be lost.
  - A unique constraint covering the columns `[aspsp_name,aspsp_country]` on the table `BankConnection` will be added. If there are existing duplicate values, this will fail.
  - Added the required column `status` to the `BankConnection` table without a default value. This is not possible if the table is not empty.
  - Added the required column `user_id` to the `BankConnection` table without a default value. This is not possible if the table is not empty.

*/
-- CreateEnum
CREATE TYPE "BankStatus" AS ENUM ('ACTIVE', 'EXPIRED', 'REVOKED');

-- DropForeignKey
ALTER TABLE "BankConnection" DROP CONSTRAINT "BankConnection_active_session_id_fkey";

-- DropIndex
DROP INDEX "BankConnection_active_session_id_aspsp_name_aspsp_country_key";

-- DropIndex
DROP INDEX "BankConnection_active_session_id_idx";

-- AlterTable
ALTER TABLE "BankConnection" DROP COLUMN "active_session_id",
ADD COLUMN     "status" "BankStatus" NOT NULL,
ADD COLUMN     "user_id" BIGINT NOT NULL;

-- CreateTable
CREATE TABLE "Account" (
    "id" BIGSERIAL NOT NULL,
    "bank_aspsp_id" BIGINT NOT NULL,
    "iban" TEXT,
    "name" TEXT,
    "currency" TEXT NOT NULL,
    "bank_connection_id" BIGINT,

    CONSTRAINT "Account_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Transaction" (
    "id" BIGSERIAL NOT NULL,
    "transaction_date" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "account_id" BIGINT,

    CONSTRAINT "Transaction_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "Account_bank_aspsp_id_key" ON "Account"("bank_aspsp_id");

-- CreateIndex
CREATE INDEX "BankConnection_user_id_idx" ON "BankConnection"("user_id");

-- CreateIndex
CREATE UNIQUE INDEX "BankConnection_aspsp_name_aspsp_country_key" ON "BankConnection"("aspsp_name", "aspsp_country");

-- AddForeignKey
ALTER TABLE "Account" ADD CONSTRAINT "Account_bank_connection_id_fkey" FOREIGN KEY ("bank_connection_id") REFERENCES "BankConnection"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "BankConnection" ADD CONSTRAINT "BankConnection_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Transaction" ADD CONSTRAINT "Transaction_account_id_fkey" FOREIGN KEY ("account_id") REFERENCES "Account"("id") ON DELETE SET NULL ON UPDATE CASCADE;
