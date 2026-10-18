CREATE TABLE "ExpectedSalary" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "month" DATE NOT NULL,
    "amount" DECIMAL(24,8) NOT NULL,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "ExpectedSalary_pkey" PRIMARY KEY ("id")
);

CREATE UNIQUE INDEX "ExpectedSalary_user_id_month_key" ON "ExpectedSalary"("user_id", "month");

ALTER TABLE "ExpectedSalary" ADD CONSTRAINT "ExpectedSalary_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;
