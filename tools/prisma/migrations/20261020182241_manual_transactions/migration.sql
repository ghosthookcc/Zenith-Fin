CREATE TABLE "ManualTransaction" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "occurred_on" DATE NOT NULL,
    "amount" DECIMAL(24,8) NOT NULL,
    "currency" VARCHAR(3) NOT NULL DEFAULT 'SEK',
    "description" TEXT,
    "counting" "CreditCounting" NOT NULL,
    "from_account_id" BIGINT,
    "from_virtual_account_id" BIGINT,
    "from_account_number" TEXT,
    "from_name" TEXT,
    "to_account_id" BIGINT,
    "to_virtual_account_id" BIGINT,
    "to_account_number" TEXT,
    "to_name" TEXT,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "ManualTransaction_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "ManualTransaction_user_id_occurred_on_idx" ON "ManualTransaction"("user_id", "occurred_on");

CREATE INDEX "ManualTransaction_from_account_id_idx" ON "ManualTransaction"("from_account_id");

CREATE INDEX "ManualTransaction_to_account_id_idx" ON "ManualTransaction"("to_account_id");

CREATE INDEX "ManualTransaction_from_virtual_account_id_idx" ON "ManualTransaction"("from_virtual_account_id");

CREATE INDEX "ManualTransaction_to_virtual_account_id_idx" ON "ManualTransaction"("to_virtual_account_id");

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_from_account_id_fkey" FOREIGN KEY ("from_account_id") REFERENCES "Account"("id") ON DELETE SET NULL ON UPDATE CASCADE;

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_from_virtual_account_id_fkey" FOREIGN KEY ("from_virtual_account_id") REFERENCES "VirtualAccount"("id") ON DELETE SET NULL ON UPDATE CASCADE;

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_to_account_id_fkey" FOREIGN KEY ("to_account_id") REFERENCES "Account"("id") ON DELETE SET NULL ON UPDATE CASCADE;

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_to_virtual_account_id_fkey" FOREIGN KEY ("to_virtual_account_id") REFERENCES "VirtualAccount"("id") ON DELETE SET NULL ON UPDATE CASCADE;


ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_amount_check" CHECK ("amount" > 0);

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_counting_check" CHECK ("counting" IN ('NEUTRAL', 'IN', 'OUT'));

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_one_source_check" CHECK (num_nonnulls("from_account_id", "from_virtual_account_id") <= 1);

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_one_destination_check" CHECK (num_nonnulls("to_account_id", "to_virtual_account_id") <= 1);

ALTER TABLE "ManualTransaction" ADD CONSTRAINT "ManualTransaction_destination_check" CHECK (num_nonnulls("to_account_id", "to_virtual_account_id", "to_account_number", "to_name") >= 1);
