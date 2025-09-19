using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    public partial class OwnerOverride_SafeOps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // تریگر: قفل کامل روی AppRoles (جز وقتی مالک با سوئیچ فعال است)
            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_AppRoles_BlockExternalDml
ON dbo.AppRoles
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ov INT = TRY_CAST(SESSION_CONTEXT(N'owner_override') AS INT);
    IF @ov IS NULL SET @ov = 0;

    IF (@ov = 1 AND ORIGINAL_LOGIN() = N'FEA_Owner')
        RETURN;

    RAISERROR('Direct modifications to AppRoles are prohibited.', 16, 1);
    ROLLBACK TRANSACTION;
END
");

            // تریگر: قفل تغییر RoleId از بیرون اپ (یا بدون سوئیچ مالک)
            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_AppUsers_BlockExternalRoleUpdate
ON dbo.AppUsers
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF (UPDATE(RoleId))
    BEGIN
        DECLARE @ov INT = TRY_CAST(SESSION_CONTEXT(N'owner_override') AS INT);
        IF @ov IS NULL SET @ov = 0;

        DECLARE @app SYSNAME = APP_NAME();

        IF NOT ( (@app = N'FinancialEvaluationApp') OR (@ov = 1 AND ORIGINAL_LOGIN() = N'FEA_Owner') )
        BEGIN
            RAISERROR('Direct modifications to AppUsers.RoleId are prohibited.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END
    END
END
");

            // تریگر: همیشه دقیقا ۱ ادمین (مالک با سوئیچ می‌تواند موقت عبور کند)
            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_AppUsers_OneAdmin
ON dbo.AppUsers
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ov INT = TRY_CAST(SESSION_CONTEXT(N'owner_override') AS INT);
    IF @ov IS NULL SET @ov = 0;

    IF (@ov = 1 AND ORIGINAL_LOGIN() = N'FEA_Owner')
        RETURN;

    DECLARE @AdminRoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.AppRoles WHERE Name = N'Admin');
    IF @AdminRoleId IS NULL RETURN;

    DECLARE @cnt INT = (SELECT COUNT(*) FROM dbo.AppUsers WHERE RoleId = @AdminRoleId);

    IF (@cnt <> 1)
    BEGIN
        RAISERROR('Exactly one Admin user is required.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END
");

            // SP: انتقال ادمین به کاربر دیگر (اتمیک)
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_SetSingleAdminByUsername
    @NewAdminUsername NVARCHAR(50),
    @DemoteOldAdminToRoleName NVARCHAR(50) = N'Manager'
AS
BEGIN
    SET NOCOUNT ON;

    -- روشن کردن سوئیچ مالک در همین سشن
    EXEC sys.sp_set_session_context @key=N'owner_override', @value=1;

    DECLARE @AdminRoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.AppRoles WHERE Name = N'Admin');
    DECLARE @DemoteRoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.AppRoles WHERE Name = @DemoteOldAdminToRoleName);
    IF @AdminRoleId IS NULL OR @DemoteRoleId IS NULL
    BEGIN
        RAISERROR('Admin/Demote role not found.', 16, 1); RETURN;
    END

    DECLARE @NewAdminId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.AppUsers WHERE Username = @NewAdminUsername);
    IF @NewAdminId IS NULL
    BEGIN
        RAISERROR('Target user not found.', 16, 1); RETURN;
    END

    DECLARE @CurrentAdminId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.AppUsers WHERE RoleId = @AdminRoleId);

    BEGIN TRAN;

    IF @CurrentAdminId IS NULL
    BEGIN
        UPDATE dbo.AppUsers SET RoleId = @AdminRoleId WHERE Id = @NewAdminId;
    END
    ELSE IF @CurrentAdminId <> @NewAdminId
    BEGIN
        UPDATE dbo.AppUsers SET RoleId = @DemoteRoleId WHERE Id = @CurrentAdminId;
        UPDATE dbo.AppUsers SET RoleId = @AdminRoleId WHERE Id = @NewAdminId;
    END

    COMMIT TRAN;

    -- خاموش کردن سوئیچ
    EXEC sys.sp_set_session_context @key=N'owner_override', @value=NULL;
END
");

            // SP: تغییر نقش کاربران (به‌جز نقش Admin)
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_UpdateRoleByUsername
    @Username NVARCHAR(50),
    @RoleName NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    IF (@RoleName = N'Admin')
    BEGIN
        RAISERROR('Use usp_SetSingleAdminByUsername for Admin changes.', 16, 1);
        RETURN;
    END

    DECLARE @TargetUserId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.AppUsers WHERE Username = @Username);
    IF @TargetUserId IS NULL
    BEGIN
        RAISERROR('User not found.', 16, 1); RETURN;
    END

    DECLARE @RoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.AppRoles WHERE Name = @RoleName);
    IF @RoleId IS NULL
    BEGIN
        RAISERROR('Role not found.', 16, 1); RETURN;
    END

    DECLARE @AdminRoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.AppRoles WHERE Name = N'Admin');

    -- جلوگیری از تنزل تنها ادمین
    IF @AdminRoleId IS NOT NULL AND @RoleId <> @AdminRoleId
       AND EXISTS (SELECT 1 FROM dbo.AppUsers WHERE Id = @TargetUserId AND RoleId = @AdminRoleId)
       AND (SELECT COUNT(*) FROM dbo.AppUsers WHERE RoleId = @AdminRoleId) = 1
    BEGIN
        RAISERROR('Cannot demote the only Admin. Use usp_SetSingleAdminByUsername.', 16, 1);
        RETURN;
    END

    -- روشن کردن سوئیچ مالک
    EXEC sys.sp_set_session_context @key=N'owner_override', @value=1;

    UPDATE dbo.AppUsers SET RoleId = @RoleId WHERE Id = @TargetUserId;

    -- خاموش کردن سوئیچ
    EXEC sys.sp_set_session_context @key=N'owner_override', @value=NULL;
END
");

            // SP: افزودن/به‌روزرسانی نقش (در صورت نیاز)
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_UpsertRole
    @Name NVARCHAR(50),
    @IsActive BIT = 1,
    @IsSystem BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    -- روشن کردن سوئیچ مالک
    EXEC sys.sp_set_session_context @key=N'owner_override', @value=1;

    IF EXISTS (SELECT 1 FROM dbo.AppRoles WHERE Name = @Name)
        UPDATE dbo.AppRoles SET IsActive = @IsActive, IsSystem = @IsSystem WHERE Name = @Name;
    ELSE
        INSERT INTO dbo.AppRoles (Id, Name, IsActive, IsSystem) VALUES (NEWID(), @Name, @IsActive, @IsSystem);

    -- خاموش کردن سوئیچ
    EXEC sys.sp_set_session_context @key=N'owner_override', @value=NULL;
END
");

            // دسترسی اجرا فقط اگر یوزر FEA_Owner وجود داشته باشد
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'FEA_Owner')
BEGIN
    GRANT EXECUTE ON dbo.usp_SetSingleAdminByUsername TO [FEA_Owner];
    GRANT EXECUTE ON dbo.usp_UpdateRoleByUsername     TO [FEA_Owner];
    GRANT EXECUTE ON dbo.usp_UpsertRole               TO [FEA_Owner];
END
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.usp_UpsertRole','P') IS NOT NULL DROP PROCEDURE dbo.usp_UpsertRole;
IF OBJECT_ID('dbo.usp_UpdateRoleByUsername','P') IS NOT NULL DROP PROCEDURE dbo.usp_UpdateRoleByUsername;
IF OBJECT_ID('dbo.usp_SetSingleAdminByUsername','P') IS NOT NULL DROP PROCEDURE dbo.usp_SetSingleAdminByUsername;
");
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.TR_AppUsers_OneAdmin','TR') IS NOT NULL DROP TRIGGER dbo.TR_AppUsers_OneAdmin;
");
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.TR_AppUsers_BlockExternalRoleUpdate','TR') IS NOT NULL DROP TRIGGER dbo.TR_AppUsers_BlockExternalRoleUpdate;
");
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.TR_AppRoles_BlockExternalDml','TR') IS NOT NULL DROP TRIGGER dbo.TR_AppRoles_BlockExternalDml;
");
        }
    }
}
