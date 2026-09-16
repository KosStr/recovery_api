using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecoveryApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnonymousAccountsAndLoginTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_apple_user_id",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "apple_user_id",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<bool>(
                name: "is_anonymous",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_login_at",
                table: "users",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_apple_user_id",
                table: "users",
                column: "apple_user_id",
                unique: true,
                filter: "apple_user_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_is_anonymous_last_login_at",
                table: "users",
                columns: new[] { "is_anonymous", "last_login_at" },
                filter: "is_anonymous = true AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_apple_user_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_is_anonymous_last_login_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_anonymous",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_login_at",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "apple_user_id",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_apple_user_id",
                table: "users",
                column: "apple_user_id",
                unique: true);
        }
    }
}
