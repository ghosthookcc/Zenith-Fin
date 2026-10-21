CREATE TYPE "ShareStatus" AS ENUM ('PENDING', 'ACCEPTED', 'DECLINED', 'CANCELLED', 'ENDED');

CREATE TABLE "Partnership" (
    "id" BIGSERIAL NOT NULL,
    "inviter_id" BIGINT NOT NULL,
    "invitee_id" BIGINT NOT NULL,
    "status" "ShareStatus" NOT NULL,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "responded_at" TIMESTAMPTZ(3),
    "ended_at" TIMESTAMPTZ(3),

    CONSTRAINT "Partnership_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "SplitRule" (
    "id" BIGSERIAL NOT NULL,
    "partnership_id" BIGINT NOT NULL,
    "inviter_share" INTEGER NOT NULL,
    "valid_from" DATE NOT NULL,
    "status" "ShareStatus" NOT NULL,
    "proposed_by" BIGINT NOT NULL,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "responded_at" TIMESTAMPTZ(3),

    CONSTRAINT "SplitRule_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "AccountSharing" (
    "id" BIGSERIAL NOT NULL,
    "partnership_id" BIGINT NOT NULL,
    "account_id" BIGINT NOT NULL,
    "owner_id" BIGINT NOT NULL,
    "status" "ShareStatus" NOT NULL,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "responded_at" TIMESTAMPTZ(3),
    "ended_at" TIMESTAMPTZ(3),

    CONSTRAINT "AccountSharing_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "Notification" (
    "id" BIGSERIAL NOT NULL,
    "user_id" BIGINT NOT NULL,
    "text" TEXT NOT NULL,
    "created_at" TIMESTAMPTZ(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "read_at" TIMESTAMPTZ(3),

    CONSTRAINT "Notification_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "Partnership_inviter_id_idx" ON "Partnership"("inviter_id");

CREATE INDEX "Partnership_invitee_id_idx" ON "Partnership"("invitee_id");

CREATE INDEX "SplitRule_partnership_id_idx" ON "SplitRule"("partnership_id");

CREATE INDEX "AccountSharing_partnership_id_idx" ON "AccountSharing"("partnership_id");

CREATE INDEX "AccountSharing_account_id_idx" ON "AccountSharing"("account_id");

CREATE INDEX "Notification_user_id_created_at_idx" ON "Notification"("user_id", "created_at");

ALTER TABLE "Partnership" ADD CONSTRAINT "Partnership_inviter_id_fkey" FOREIGN KEY ("inviter_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "Partnership" ADD CONSTRAINT "Partnership_invitee_id_fkey" FOREIGN KEY ("invitee_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "SplitRule" ADD CONSTRAINT "SplitRule_partnership_id_fkey" FOREIGN KEY ("partnership_id") REFERENCES "Partnership"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "AccountSharing" ADD CONSTRAINT "AccountSharing_partnership_id_fkey" FOREIGN KEY ("partnership_id") REFERENCES "Partnership"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "AccountSharing" ADD CONSTRAINT "AccountSharing_account_id_fkey" FOREIGN KEY ("account_id") REFERENCES "Account"("id") ON DELETE CASCADE ON UPDATE CASCADE;

ALTER TABLE "Notification" ADD CONSTRAINT "Notification_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;


ALTER TABLE "Partnership" ADD CONSTRAINT "Partnership_two_people_check" CHECK ("inviter_id" <> "invitee_id");

CREATE UNIQUE INDEX "Partnership_one_open_per_pair" ON "Partnership" (LEAST("inviter_id", "invitee_id"), GREATEST("inviter_id", "invitee_id")) WHERE "status" IN ('PENDING', 'ACCEPTED');

ALTER TABLE "SplitRule" ADD CONSTRAINT "SplitRule_share_check" CHECK ("inviter_share" BETWEEN 0 AND 10000);

CREATE UNIQUE INDEX "SplitRule_one_active_per_date" ON "SplitRule" ("partnership_id", "valid_from") WHERE "status" = 'ACCEPTED';

CREATE UNIQUE INDEX "AccountSharing_one_open_per_account" ON "AccountSharing" ("account_id") WHERE "status" IN ('PENDING', 'ACCEPTED');
