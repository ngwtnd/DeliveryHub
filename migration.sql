IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [FullName] nvarchar(100) NOT NULL,
    [Address] nvarchar(max) NULL,
    [Latitude] float NULL,
    [Longitude] float NULL,
    [Role] int NOT NULL,
    [AvatarUrl] nvarchar(max) NULL,
    [Balance] decimal(18,2) NOT NULL,
    [IsActive] bit NOT NULL,
    [IsDelivering] bit NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Orders] (
    [Id] int NOT NULL IDENTITY,
    [OrderCode] nvarchar(50) NOT NULL,
    [UserId] nvarchar(450) NOT NULL,
    [ShipperId] nvarchar(450) NULL,
    [Status] int NOT NULL,
    [TotalPrice] decimal(18,2) NOT NULL,
    [ShippingFee] decimal(18,2) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [PickupAddress] nvarchar(max) NOT NULL,
    [DeliveryAddress] nvarchar(max) NOT NULL,
    [PickupLatitude] float NOT NULL,
    [PickupLongitude] float NOT NULL,
    [DeliveryLatitude] float NOT NULL,
    [DeliveryLongitude] float NOT NULL,
    [PaymentMethod] int NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Orders_AspNetUsers_ShipperId] FOREIGN KEY ([ShipperId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Orders_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Stores] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [Address] nvarchar(max) NULL,
    [ImageUrl] nvarchar(max) NULL,
    [Latitude] float NOT NULL,
    [Longitude] float NOT NULL,
    [OwnerId] nvarchar(450) NOT NULL,
    [IsOpen] bit NOT NULL,
    [Rating] float NOT NULL,
    CONSTRAINT [PK_Stores] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Stores_AspNetUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [AspNetUsers] ([Id])
);

CREATE TABLE [ChatMessages] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [SenderId] nvarchar(max) NOT NULL,
    [ReceiverId] nvarchar(max) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [Timestamp] datetime2 NOT NULL,
    CONSTRAINT [PK_ChatMessages] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ChatMessages_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Reviews] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [RatingMenu] int NOT NULL,
    [RatingShipper] int NOT NULL,
    [Comment] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Reviews] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Reviews_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [MenuItems] (
    [Id] int NOT NULL IDENTITY,
    [StoreId] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    [ImageUrl] nvarchar(max) NULL,
    [IsAvailable] bit NOT NULL,
    CONSTRAINT [PK_MenuItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MenuItems_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [OrderItems] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [MenuItemId] int NOT NULL,
    [Quantity] int NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_OrderItems_MenuItems_MenuItemId] FOREIGN KEY ([MenuItemId]) REFERENCES [MenuItems] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_ChatMessages_OrderId] ON [ChatMessages] ([OrderId]);

CREATE INDEX [IX_MenuItems_StoreId] ON [MenuItems] ([StoreId]);

CREATE INDEX [IX_OrderItems_MenuItemId] ON [OrderItems] ([MenuItemId]);

CREATE INDEX [IX_OrderItems_OrderId] ON [OrderItems] ([OrderId]);

CREATE UNIQUE INDEX [IX_Orders_OrderCode] ON [Orders] ([OrderCode]);

CREATE INDEX [IX_Orders_ShipperId] ON [Orders] ([ShipperId]);

CREATE INDEX [IX_Orders_UserId] ON [Orders] ([UserId]);

CREATE INDEX [IX_Reviews_OrderId] ON [Reviews] ([OrderId]);

CREATE INDEX [IX_Stores_OwnerId] ON [Stores] ([OwnerId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260402074747_InitialCreate', N'10.0.5');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [Orders] ADD [AcceptedAt] datetime2 NULL;

ALTER TABLE [Orders] ADD [CancelledAt] datetime2 NULL;

ALTER TABLE [Orders] ADD [CompletedAt] datetime2 NULL;

ALTER TABLE [Orders] ADD [Distance] float NOT NULL DEFAULT 0.0E0;

ALTER TABLE [Orders] ADD [PickedUpAt] datetime2 NULL;

ALTER TABLE [Orders] ADD [StoreId] int NULL;

CREATE INDEX [IX_Orders_StoreId] ON [Orders] ([StoreId]);

ALTER TABLE [Orders] ADD CONSTRAINT [FK_Orders_Stores_StoreId] FOREIGN KEY ([StoreId]) REFERENCES [Stores] ([Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260402111115_AddDetailedOrderFields', N'10.0.5');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [Orders] ADD [IsPinned] bit NOT NULL DEFAULT CAST(0 AS bit);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260402113511_AddPinnedStatus', N'10.0.5');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [Orders] ADD [Note] nvarchar(max) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260402124942_AddOrderNote', N'10.0.5');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [MenuItems] ADD [Category] nvarchar(50) NOT NULL DEFAULT N'';

ALTER TABLE [AspNetUsers] ADD [CitizenId] nvarchar(max) NULL;

ALTER TABLE [AspNetUsers] ADD [CreatedAt] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260403112507_AddShipperDetails', N'10.0.5');

COMMIT;
GO

