ALTER TABLE "VirtualAccount" ADD COLUMN     "balance" DECIMAL(24,8),
ADD COLUMN     "balance_set_at" TIMESTAMPTZ(3);

