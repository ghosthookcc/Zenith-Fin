/*
  Warnings:

  - You are about to drop the `AspspSession` table. If the table is not empty, all the data it contains will be lost.

*/
-- DropForeignKey
ALTER TABLE "AspspSession" DROP CONSTRAINT "AspspSession_active_session_id_fkey";

-- DropTable
DROP TABLE "AspspSession";

-- CreateTable
CREATE TABLE "PendingBankConnection" (
    "id" BIGSERIAL NOT NULL,
    "active_session_id" UUID NOT NULL,
    "state" TEXT NOT NULL,
    "aspsp_name" TEXT NOT NULL,
    "aspsp_country" TEXT NOT NULL,
    "psu_type" TEXT DEFAULT 'personal',
    "expires_at" TIMESTAMP(3) NOT NULL,
    "auth_expires_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "PendingBankConnection_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "BankConnection" (
    "id" BIGSERIAL NOT NULL,
    "active_session_id" UUID NOT NULL,
    "aspsp_authorization_id" TEXT NOT NULL,
    "aspsp_name" TEXT NOT NULL,
    "aspsp_country" TEXT NOT NULL,
    "psu_type" TEXT NOT NULL,
    "access_token_encrypted" TEXT NOT NULL,
    "refresh_token_encrypted" TEXT NOT NULL,
    "token_expires_at" TIMESTAMP(3) NOT NULL,
    "consent_expires_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "BankConnection_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "PendingBankConnection_state_key" ON "PendingBankConnection"("state");

-- CreateIndex
CREATE INDEX "PendingBankConnection_active_session_id_idx" ON "PendingBankConnection"("active_session_id");

-- CreateIndex
CREATE INDEX "PendingBankConnection_state_idx" ON "PendingBankConnection"("state");

-- CreateIndex
CREATE UNIQUE INDEX "BankConnection_aspsp_authorization_id_key" ON "BankConnection"("aspsp_authorization_id");

-- CreateIndex
CREATE UNIQUE INDEX "BankConnection_access_token_encrypted_key" ON "BankConnection"("access_token_encrypted");

-- CreateIndex
CREATE UNIQUE INDEX "BankConnection_refresh_token_encrypted_key" ON "BankConnection"("refresh_token_encrypted");

-- CreateIndex
CREATE INDEX "BankConnection_active_session_id_idx" ON "BankConnection"("active_session_id");

-- CreateIndex
CREATE UNIQUE INDEX "BankConnection_active_session_id_aspsp_name_aspsp_country_key" ON "BankConnection"("active_session_id", "aspsp_name", "aspsp_country");

-- AddForeignKey
ALTER TABLE "PendingBankConnection" ADD CONSTRAINT "PendingBankConnection_active_session_id_fkey" FOREIGN KEY ("active_session_id") REFERENCES "Session"("session_id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "BankConnection" ADD CONSTRAINT "BankConnection_active_session_id_fkey" FOREIGN KEY ("active_session_id") REFERENCES "Session"("session_id") ON DELETE RESTRICT ON UPDATE CASCADE;
