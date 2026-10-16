using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Askwerk.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Closing date and one response per person on the survey (AWK-29, W24). Both are columns of survey, so the
    /// table's existing row-level security policy (survey_tenant) covers them; no new table, no new policy. Existing
    /// surveys keep today's behaviour: no closing date, one response per browser.
    /// </summary>
    public partial class CollectingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closes_at",
                table: "survey",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "one_response_per_person",
                table: "survey",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closes_at",
                table: "survey");

            migrationBuilder.DropColumn(
                name: "one_response_per_person",
                table: "survey");
        }
    }
}
