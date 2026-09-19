using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    public partial class AddCategories : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category_key",
                table: "expenses",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    icon_name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    keywords_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_group_id_category_key",
                table: "expenses",
                columns: new[] { "group_id", "category_key" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_group_id_key",
                table: "categories",
                columns: new[] { "group_id", "key" },
                unique: true);

            migrationBuilder.InsertData(
                table: "categories",
                columns: ["id", "key", "name", "icon_name", "color_hex", "sort_order", "keywords_json"],
                values: new object[,]
                {
                    { new Guid("5a16a600-59fb-e597-5ab6-ecaaa3d80552"), "groceries", "Groceries", "cart-shopping", "#16a34a", 10, @"[""epicerie"", ""grocery"", ""metro"", ""iga"", ""maxi"", ""provigo"", ""loblaw"", ""superstore"", ""costco"", ""walmart"", ""supermarche"", ""marche"", ""lufa""]" },
                    { new Guid("6ab58c30-a310-4065-d6e0-1f280635e51d"), "dining", "Dining out", "utensils", "#f97316", 20, @"[""restaurant"", ""resto"", ""cafe"", ""bar "", ""pub"", ""uber eats"", ""doordash"", ""skip the dishes"", ""skipthedishes"", ""starbucks"", ""tim hortons"", ""mcdonald"", ""pizza"", ""sushi"", ""brasserie"", ""depanneur""]" },
                    { new Guid("77b13eee-a3c3-8154-5a34-5e9539f0bf64"), "transport", "Transport", "car", "#0ea5e9", 30, @"[""uber"", ""lyft"", ""taxi"", ""communauto"", ""bixi"", ""stm"", ""exo"", ""via rail"", ""essence"", ""petro"", ""shell"", ""esso"", ""ultramar"", ""parking"", ""stationnement"", ""garage"", ""pneus""]" },
                    { new Guid("7cec333b-6318-d2dd-a7fa-5f42b186f875"), "housing", "Rent and housing", "house", "#8b5cf6", 40, @"[""loyer"", ""rent"", ""hypotheque"", ""mortgage"", ""bail"", ""assurance habitation"", ""concierge"", ""reno"", ""quincaillerie"", ""rona"", ""home depot"", ""ikea""]" },
                    { new Guid("e06dc9ab-849e-60b9-46f6-b6f3b6f358c3"), "utilities", "Bills", "bolt", "#eab308", 50, @"[""hydro"", ""gaz metro"", ""energir"", ""bell"", ""videotron"", ""rogers"", ""telus"", ""fizz"", ""koodo"", ""internet"", ""telephone"", ""facture""]" },
                    { new Guid("18437ed8-c102-49f7-1afe-5b820ece905d"), "entertainment", "Fun", "ticket-simple", "#ec4899", 60, @"[""cinema"", ""cineplex"", ""spectacle"", ""billet"", ""ticket"", ""steam"", ""jeu"", ""musee"", ""theatre"", ""concert"", ""escalade"", ""ski""]" },
                    { new Guid("4cb7b55a-ddc3-6963-10db-3c159fefc3c1"), "travel", "Travel", "plane", "#06b6d4", 70, @"[""air canada"", ""airbnb"", ""booking.com"", ""hotel"", ""auberge"", ""vol "", ""flight"", ""west jet"", ""westjet"", ""porter"", ""camping"", ""sepaq""]" },
                    { new Guid("2975c868-1d4a-dc06-7ad7-1b36ec58b24a"), "health", "Health", "kit-medical", "#ef4444", 80, @"[""pharmaprix"", ""jean coutu"", ""pharmacie"", ""pharmacy"", ""clinique"", ""dentiste"", ""optometriste"", ""physio"", ""lunettes""]" },
                    { new Guid("b30bf7ed-6591-f999-142b-5a60fbe04b64"), "shopping", "Shopping", "bag-shopping", "#a855f7", 90, @"[""amazon"", ""amzn"", ""simons"", ""winners"", ""decathlon"", ""sports experts"", ""vetements"", ""chaussures"", ""canadian tire""]" },
                    { new Guid("d37cd39c-ad19-00d1-93e6-d3f9f8d05d59"), "subscriptions", "Subscriptions", "calendar-days", "#6366f1", 100, @"[""netflix"", ""spotify"", ""disney"", ""crave"", ""apple.com/bill"", ""icloud"", ""google storage"", ""abonnement"", ""patreon"", ""youtube premium""]" },
                    { new Guid("50c224ff-ca2a-683e-9e37-507cf6fd5b1b"), "pets", "Pets", "paw", "#84cc16", 110, @"[""mondou"", ""veterinaire"", ""veto"", ""animalerie"", ""croquettes""]" },
                    { new Guid("3f1e96e9-106c-5b93-16f7-0b2d807442ea"), "gifts", "Gifts", "gift", "#f43f5e", 120, @"[""cadeau"", ""gift"", ""anniversaire"", ""noel""]" },
                    { new Guid("4b395484-d4e0-24c0-dfe6-11c34855ae58"), "fees", "Fees", "building-columns", "#64748b", 130, @"[""frais"", ""interet"", ""interest"", ""service charge"", ""nsf"", ""penalite""]" },
                    { new Guid("98b8ca34-c5de-554a-c0a3-846ced9cd476"), "other", "Other", "ellipsis", "#94a3b8", 999, @"[]" },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropIndex(
                name: "ix_expenses_group_id_category_key",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "category_key",
                table: "expenses");
        }
    }
}
