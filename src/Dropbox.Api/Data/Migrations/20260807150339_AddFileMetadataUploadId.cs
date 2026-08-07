using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dropbox.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileMetadataUploadId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UploadId",
                table: "Files",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Files_Fingerprint",
                table: "Files",
                column: "Fingerprint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Files_Fingerprint",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "UploadId",
                table: "Files");
        }
    }
}
