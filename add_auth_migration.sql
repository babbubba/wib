-- Add UserId column to Receipts table
ALTER TABLE "Receipts" ADD COLUMN "UserId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

-- Create Users table
CREATE TABLE "Users" (
    "Id" uuid NOT NULL,
    "Email" text NOT NULL,
    "PasswordHash" text NOT NULL,
    "FirstName" text NOT NULL,
    "LastName" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "LastLoginAt" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "EmailVerified" boolean NOT NULL,
    "EmailVerificationToken" text,
    "PasswordResetToken" text,
    "PasswordResetTokenExpiry" timestamp with time zone,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);

-- Create RefreshTokens table
CREATE TABLE "RefreshTokens" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Token" text NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "IsRevoked" boolean NOT NULL,
    "RevokedReason" text,
    "DeviceInfo" text,
    "IpAddress" text,
    CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id")
);

-- Create indexes
CREATE INDEX "IX_Receipts_UserId" ON "Receipts" ("UserId");
CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");
CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

-- Add foreign key constraints
ALTER TABLE "Receipts" ADD CONSTRAINT "FK_Receipts_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE;
ALTER TABLE "RefreshTokens" ADD CONSTRAINT "FK_RefreshTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE;

-- Insert migration record
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20251012183336_AddUserAuthentication', '8.0.8');