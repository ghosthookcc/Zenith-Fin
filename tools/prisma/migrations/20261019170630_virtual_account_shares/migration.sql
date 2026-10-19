ALTER TABLE "AccountShare" ADD COLUMN     "virtual_account_id" BIGINT,
ALTER COLUMN "account_id" DROP NOT NULL;

CREATE INDEX "AccountShare_virtual_account_id_idx" ON "AccountShare"("virtual_account_id");

ALTER TABLE "AccountShare" ADD CONSTRAINT "AccountShare_virtual_account_id_fkey" FOREIGN KEY ("virtual_account_id") REFERENCES "VirtualAccount"("id") ON DELETE CASCADE ON UPDATE CASCADE;


ALTER TABLE "AccountShare" ADD CONSTRAINT "AccountShare_one_target_check" CHECK (num_nonnulls("account_id", "virtual_account_id") = 1);
