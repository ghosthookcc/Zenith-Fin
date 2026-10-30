CREATE TYPE "AvanzaSyncState" AS ENUM ('OK', 'AUTH_FAILED', 'ERROR', 'KEY_MISMATCH');

CREATE TABLE "avanza_credentials" (
    "user_id" BIGINT NOT NULL,
    "ciphertext" BYTEA NOT NULL,
    "key_fingerprint" VARCHAR(64) NOT NULL,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "avanza_credentials_pkey" PRIMARY KEY ("user_id")
);

CREATE TABLE "avanza_balance_snapshots" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "account_id" VARCHAR(64) NOT NULL,
    "account_name" VARCHAR(200) NOT NULL,
    "account_type" VARCHAR(64),
    "total_value" DECIMAL(18,2) NOT NULL,
    "currency" VARCHAR(3) NOT NULL,
    "fetched_at" TIMESTAMPTZ(3) NOT NULL,

    CONSTRAINT "avanza_balance_snapshots_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "avanza_sync_status" (
    "user_id" BIGINT NOT NULL,
    "status" "AvanzaSyncState",
    "error_class" VARCHAR(100),
    "last_attempt_at" TIMESTAMPTZ(3),
    "last_success_at" TIMESTAMPTZ(3),

    CONSTRAINT "avanza_sync_status_pkey" PRIMARY KEY ("user_id")
);

CREATE INDEX "avanza_balance_snapshots_user_id_fetched_at_idx" ON "avanza_balance_snapshots"("user_id", "fetched_at");

ALTER TABLE "avanza_credentials" ADD CONSTRAINT "avanza_credentials_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "avanza_balance_snapshots" ADD CONSTRAINT "avanza_balance_snapshots_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "avanza_sync_status" ADD CONSTRAINT "avanza_sync_status_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;
