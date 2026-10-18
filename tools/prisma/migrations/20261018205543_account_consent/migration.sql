ALTER TABLE "Account" ADD COLUMN     "identification_hash" TEXT,
ADD COLUMN     "in_consent" BOOLEAN NOT NULL DEFAULT true;
