CREATE TABLE "SharedArrangement" (
    "id" BIGSERIAL NOT NULL,
    "partnership_id" BIGINT NOT NULL,
    "account_id" BIGINT NOT NULL,
    "payer_id" BIGINT NOT NULL,
    "signature" TEXT NOT NULL,
    "credit_debit_indicator" TEXT NOT NULL,
    "label" TEXT NOT NULL,
    "valid_from" DATE NOT NULL,
    "status" "ShareStatus" NOT NULL,
    "proposed_by" BIGINT NOT NULL,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "responded_at" TIMESTAMPTZ(3),
    "ended_at" TIMESTAMPTZ(3),

    CONSTRAINT "SharedArrangement_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "SharedArrangement_partnership_id_idx" ON "SharedArrangement"("partnership_id");

CREATE INDEX "SharedArrangement_account_id_idx" ON "SharedArrangement"("account_id");

ALTER TABLE "SharedArrangement" ADD CONSTRAINT "SharedArrangement_partnership_id_fkey" FOREIGN KEY ("partnership_id") REFERENCES "Partnership"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "SharedArrangement" ADD CONSTRAINT "SharedArrangement_account_id_fkey" FOREIGN KEY ("account_id") REFERENCES "Account"("id") ON DELETE CASCADE ON UPDATE CASCADE;


CREATE UNIQUE INDEX "SharedArrangement_one_open_per_payment" ON "SharedArrangement" ("account_id", "signature", "credit_debit_indicator") WHERE "status" IN ('PENDING', 'ACCEPTED');
