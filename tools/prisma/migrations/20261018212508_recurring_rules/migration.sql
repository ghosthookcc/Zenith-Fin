CREATE TABLE "RecurringRule" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "credit_debit_indicator" TEXT NOT NULL,
    "own_transfer" BOOLEAN NOT NULL DEFAULT false,
    "signature" TEXT NOT NULL,
    "recurring" BOOLEAN NOT NULL,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "RecurringRule_pkey" PRIMARY KEY ("id")
);

CREATE UNIQUE INDEX "RecurringRule_user_id_credit_debit_indicator_own_transfer_s_key" ON "RecurringRule"("user_id", "credit_debit_indicator", "own_transfer", "signature");

ALTER TABLE "RecurringRule" ADD CONSTRAINT "RecurringRule_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;
