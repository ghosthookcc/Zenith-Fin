CREATE TABLE "RecurringPlanItemPart" (
    "id" BIGSERIAL NOT NULL,
    "item_id" BIGINT NOT NULL,
    "name" VARCHAR(60) NOT NULL,
    "amount" DECIMAL(18,2) NOT NULL,
    "position" INTEGER NOT NULL DEFAULT 0,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "RecurringPlanItemPart_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "RecurringPlanItemPart_item_id_idx" ON "RecurringPlanItemPart"("item_id");

ALTER TABLE "RecurringPlanItemPart" ADD CONSTRAINT "RecurringPlanItemPart_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "RecurringPlanItem"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "RecurringPlanItemPart" ADD CONSTRAINT "RecurringPlanItemPart_amount_check" CHECK (amount > 0);

ALTER TABLE "RecurringPlanItemPart" ADD CONSTRAINT "RecurringPlanItemPart_name_check" CHECK (btrim(name) <> '');
