ALTER TYPE "TagSource" ADD VALUE 'LEARNED';

CREATE TABLE "TagCorrection" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "credit_debit_indicator" TEXT NOT NULL,
    "own_transfer" BOOLEAN NOT NULL DEFAULT false,
    "signature" TEXT NOT NULL,
    "description" TEXT NOT NULL,
    "example" TEXT NOT NULL,
    "tag_id" BIGINT NOT NULL,
    "replaced_tag_id" BIGINT,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "TagCorrection_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "TagCorrection_tag_id_idx" ON "TagCorrection"("tag_id");

CREATE UNIQUE INDEX "TagCorrection_user_id_credit_debit_indicator_own_transfer_s_key" ON "TagCorrection"("user_id", "credit_debit_indicator", "own_transfer", "signature");

ALTER TABLE "TagCorrection" ADD CONSTRAINT "TagCorrection_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "TagCorrection" ADD CONSTRAINT "TagCorrection_tag_id_fkey" FOREIGN KEY ("tag_id") REFERENCES "Tag"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "TagCorrection" ADD CONSTRAINT "TagCorrection_replaced_tag_id_fkey" FOREIGN KEY ("replaced_tag_id") REFERENCES "Tag"("id") ON DELETE SET NULL ON UPDATE CASCADE;
