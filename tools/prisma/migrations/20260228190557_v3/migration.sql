/*
  Warnings:

  - You are about to drop the column `access_token_encrypted` on the `BankConnection` table. All the data in the column will be lost.
  - You are about to drop the column `aspsp_authorization_id` on the `BankConnection` table. All the data in the column will be lost.
  - You are about to drop the column `refresh_token_encrypted` on the `BankConnection` table. All the data in the column will be lost.
  - You are about to drop the column `token_expires_at` on the `BankConnection` table. All the data in the column will be lost.
  - You are about to drop the column `expires_at` on the `PendingBankConnection` table. All the data in the column will be lost.
  - A unique constraint covering the columns `[aspsp_session_id]` on the table `BankConnection` will be added. If there are existing duplicate values, this will fail.
  - Added the required column `aspsp_session_id` to the `BankConnection` table without a default value. This is not possible if the table is not empty.

*/
-- DropIndex
DROP INDEX "BankConnection_access_token_encrypted_key";

-- DropIndex
DROP INDEX "BankConnection_aspsp_authorization_id_key";

-- DropIndex
DROP INDEX "BankConnection_refresh_token_encrypted_key";

-- AlterTable
ALTER TABLE "BankConnection" DROP COLUMN "access_token_encrypted",
DROP COLUMN "aspsp_authorization_id",
DROP COLUMN "refresh_token_encrypted",
DROP COLUMN "token_expires_at",
ADD COLUMN     "aspsp_session_id" TEXT NOT NULL;

-- AlterTable
ALTER TABLE "PendingBankConnection" DROP COLUMN "expires_at";

-- CreateIndex
CREATE UNIQUE INDEX "BankConnection_aspsp_session_id_key" ON "BankConnection"("aspsp_session_id");
