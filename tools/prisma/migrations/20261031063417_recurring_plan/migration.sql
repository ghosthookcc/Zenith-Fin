CREATE TYPE "PlanDirection" AS ENUM ('IN', 'OUT');

CREATE TYPE "PlanUnit" AS ENUM ('MONTH', 'YEAR');

CREATE TABLE "RecurringPlan" (
    "user_id" BIGINT NOT NULL,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "RecurringPlan_pkey" PRIMARY KEY ("user_id")
);

CREATE TABLE "RecurringPlanItem" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "direction" "PlanDirection",
    "name" VARCHAR(60),
    "amount" DECIMAL(18,2),
    "every" INTEGER,
    "unit" "PlanUnit",
    "last_date" DATE,
    "end_date" DATE,
    "series_key" TEXT,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "RecurringPlanItem_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "RecurringPlanItem_user_id_idx" ON "RecurringPlanItem"("user_id");

CREATE UNIQUE INDEX "RecurringPlanItem_user_id_series_key_key" ON "RecurringPlanItem"("user_id", "series_key");

ALTER TABLE "RecurringPlan" ADD CONSTRAINT "RecurringPlan_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "RecurringPlanItem" ADD CONSTRAINT "RecurringPlanItem_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "RecurringPlanItem" ADD CONSTRAINT "RecurringPlanItem_kind_check" CHECK (series_key IS NOT NULL OR (direction IS NOT NULL AND amount IS NOT NULL AND every IS NOT NULL AND unit IS NOT NULL AND last_date IS NOT NULL));

ALTER TABLE "RecurringPlanItem" ADD CONSTRAINT "RecurringPlanItem_cadence_check" CHECK ((every IS NULL) = (unit IS NULL) AND (every IS NULL OR every BETWEEN 1 AND 120));

ALTER TABLE "RecurringPlanItem" ADD CONSTRAINT "RecurringPlanItem_amount_check" CHECK (amount IS NULL OR amount > 0);

ALTER TABLE "RecurringPlanItem" ADD CONSTRAINT "RecurringPlanItem_dates_check" CHECK (end_date IS NULL OR last_date IS NULL OR end_date >= last_date);
