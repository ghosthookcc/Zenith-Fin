CREATE TABLE "AccountShare" (
    "id" BIGSERIAL NOT NULL,
    "account_id" BIGINT NOT NULL,
    "first_name" TEXT NOT NULL,
    "last_name" TEXT NOT NULL,
    "personal_number_encrypted" TEXT,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "AccountShare_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "AccountShare_account_id_idx" ON "AccountShare"("account_id");

ALTER TABLE "AccountShare" ADD CONSTRAINT "AccountShare_account_id_fkey" FOREIGN KEY ("account_id") REFERENCES "Account"("id") ON DELETE CASCADE ON UPDATE CASCADE;

