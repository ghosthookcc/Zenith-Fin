CREATE TABLE "VirtualAccount" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "name" TEXT NOT NULL,
    "account_number" TEXT NOT NULL,
    "clearing_number" TEXT,
    "iban" TEXT,
    "counting" "CreditCounting" NOT NULL DEFAULT 'NEUTRAL',
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "VirtualAccount_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "VirtualAccount_user_id_idx" ON "VirtualAccount"("user_id");

CREATE UNIQUE INDEX "VirtualAccount_user_id_name_key" ON "VirtualAccount"("user_id", "name");

ALTER TABLE "VirtualAccount" ADD CONSTRAINT "VirtualAccount_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;
