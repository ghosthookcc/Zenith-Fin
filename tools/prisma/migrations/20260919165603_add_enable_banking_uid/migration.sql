/*
  Warnings:

  - A unique constraint covering the columns `[enable_banking_uid]` on the table `Account` will be added. If there are existing duplicate values, this will fail.
  - Added the required column `enable_banking_uid` to the `Account` table without a default value. This is not possible if the table is not empty.

*/
-- DropIndex
DROP INDEX "Account_bank_aspsp_id_key";

-- AlterTable
ALTER TABLE "Account" ADD COLUMN     "enable_banking_uid" UUID NOT NULL,
ALTER COLUMN "bank_aspsp_id" DROP NOT NULL;

-- CreateIndex
CREATE UNIQUE INDEX "Account_enable_banking_uid_key" ON "Account"("enable_banking_uid");
