CREATE TYPE "TagKind" AS ENUM ('SPENDING', 'INCOME', 'TRANSFER');

CREATE TYPE "TagSource" AS ENUM ('RULE', 'AI', 'USER');

ALTER TABLE "Transaction" ADD COLUMN     "tag_id" BIGINT,
ADD COLUMN     "tag_source" "TagSource",
ADD COLUMN     "tagged_at" TIMESTAMPTZ(3);

CREATE TABLE "Tag" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT,
    "key" TEXT,
    "name" TEXT NOT NULL,
    "icon" TEXT NOT NULL,
    "color" VARCHAR(7) NOT NULL,
    "kind" "TagKind" NOT NULL DEFAULT 'SPENDING',
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "Tag_pkey" PRIMARY KEY ("id")
);

CREATE UNIQUE INDEX "Tag_key_key" ON "Tag"("key");

CREATE INDEX "Tag_user_id_idx" ON "Tag"("user_id");

CREATE UNIQUE INDEX "Tag_user_id_name_key" ON "Tag"("user_id", "name");

CREATE INDEX "Transaction_tag_id_idx" ON "Transaction"("tag_id");

ALTER TABLE "Tag" ADD CONSTRAINT "Tag_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "Transaction" ADD CONSTRAINT "Transaction_tag_id_fkey" FOREIGN KEY ("tag_id") REFERENCES "Tag"("id") ON DELETE SET NULL ON UPDATE CASCADE;

INSERT INTO "Tag" ("key", "name", "icon", "color", "kind") VALUES
    ('groceries', 'Groceries', 'cart', '#199e70', 'SPENDING'),
    ('dining', 'Dining', 'utensils', '#d95926', 'SPENDING'),
    ('transport', 'Transport', 'bus', '#3987e5', 'SPENDING'),
    ('housing', 'Housing', 'home', '#9085e9', 'SPENDING'),
    ('bills', 'Bills & insurance', 'bolt', '#c98500', 'SPENDING'),
    ('shopping', 'Shopping', 'bag', '#d55181', 'SPENDING'),
    ('entertainment', 'Entertainment', 'film', '#e66767', 'SPENDING'),
    ('health', 'Health & fitness', 'pulse', '#2fa84f', 'SPENDING'),
    ('travel', 'Travel', 'plane', '#3987e5', 'SPENDING'),
    ('savings', 'Savings & investing', 'trend', '#9085e9', 'SPENDING'),
    ('fees', 'Fees & taxes', 'receipt', '#c98500', 'SPENDING'),
    ('income', 'Income', 'income', '#8fd19e', 'INCOME'),
    ('transfers', 'Transfers', 'transfer', '#8792a5', 'TRANSFER'),
    ('other', 'Other', 'tag', '#7b8496', 'SPENDING');
