using LogiFlow.Domain.Catalog;
using LogiFlow.Domain.Customers;
using LogiFlow.Domain.Inventory;
using LogiFlow.Domain.Results;
using LogiFlow.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LogiFlow.Infrastructure.Persistence.Seed;

/// <summary>
/// Populates a fresh database with enough realistic data to exercise every query in the course.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not <c>HasData</c>.</b> EF Core's model-level seeding bakes rows into
/// migrations, which means every row needs a hard-coded primary key, changing one row generates
/// a migration, and the data ships to production whether you want it or not. Runtime seeding
/// keeps sample data out of the schema history entirely.
/// </para>
/// <para>
/// <b>The aggregates are built through their real factories</b>, not by setting properties. That
/// means the seed data is guaranteed valid by the same rules the application enforces — and if
/// someone tightens an invariant, the seeder fails loudly instead of quietly inserting rows the
/// domain would now reject.
/// </para>
/// </remarks>
/// <param name="context">The scoped session.</param>
/// <param name="logger">Logger.</param>
public sealed class DatabaseSeeder(LogiFlowDbContext context, ILogger<DatabaseSeeder> logger)
{
    /// <summary>Seeds the database if it is empty. Safe to call on every startup.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Idempotency guard. Startup seeding runs on every boot, and on every instance when you
        // scale out - without this you would get duplicate SKUs and a unique-index violation.
        if (await context.Products.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("Database already seeded; skipping");
            return;
        }

        logger.LogInformation("Seeding database");

        List<Product> products = SeedProducts();
        context.Products.AddRange(products);

        List<Customer> customers = SeedCustomers();
        context.Customers.AddRange(customers);

        List<Warehouse> warehouses = SeedWarehouses(products);
        context.Warehouses.AddRange(warehouses);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded {ProductCount} products, {CustomerCount} customers, {WarehouseCount} warehouses",
            products.Count,
            customers.Count,
            warehouses.Count);
    }

    private static List<Product> SeedProducts()
    {
        (string Sku, string Name, decimal Price, int Grams)[] catalogue =
        [
            ("ELE-100001", "Wireless Barcode Scanner", 189.00m, 320),
            ("ELE-100002", "Rugged Warehouse Tablet 10\"", 749.00m, 850),
            ("ELE-100003", "Thermal Label Printer", 415.50m, 2400),
            ("ELE-100004", "RFID Gate Reader", 1_250.00m, 6800),
            ("PAC-200001", "Corrugated Box 400x300x300 (x50)", 42.90m, 9500),
            ("PAC-200002", "Pallet Wrap Roll 500mm (x6)", 58.00m, 12_000),
            ("PAC-200003", "Void Fill Paper 380mm", 31.25m, 7200),
            ("PAC-200004", "Fragile Tape 48mm (x36)", 74.40m, 4300),
            ("SAF-300001", "Hi-Vis Vest Class 2", 12.75m, 180),
            ("SAF-300002", "Steel Toe Boots EU42", 89.90m, 1600),
            ("SAF-300003", "Cut-Resistant Gloves (x10)", 46.00m, 900),
            ("HND-400001", "Hand Pallet Truck 2500kg", 389.00m, 68_000),
            ("HND-400002", "Folding Platform Trolley", 142.00m, 14_500),
            ("HND-400003", "Stretch Wrap Dispenser", 67.80m, 2100),
            ("STO-500001", "Steel Shelving Bay 2000x1000", 214.00m, 42_000),
            ("STO-500002", "Stackable Euro Container 600x400", 18.60m, 1900),
            ("STO-500003", "Louvre Panel Bin Kit", 96.50m, 8700),
        ];

        List<Product> products = [];

        foreach ((string skuText, string name, decimal price, int grams) in catalogue)
        {
            Sku sku = Sku.Create(skuText).Value;
            var money = new Money(price, Currency.Eur);
            Weight weight = Weight.FromGrams(grams).Value;

            Result<Product> product = Product.Create(sku, name, null, money, weight);

            // .Value on a Result throws if the factory rejected the input. In seed code that is
            // exactly right: a typo in the table above should stop startup, not insert bad data.
            products.Add(product.Value);
        }

        return products;
    }

    private static List<Customer> SeedCustomers()
    {
        (string Company, string Email, CustomerTier Tier, string City, string Country)[] accounts =
        [
            ("Rossi Logistica SRL", "ops@rossilogistica.it", CustomerTier.Platinum, "Milano", "IT"),
            ("Nordwind Handel GmbH", "einkauf@nordwind.de", CustomerTier.Gold, "Hamburg", "DE"),
            ("Atlantic Freight Ltd", "orders@atlanticfreight.co.uk", CustomerTier.Gold, "Bristol", "GB"),
            ("Beaulieu Distribution SA", "achats@beaulieu.fr", CustomerTier.Silver, "Lyon", "FR"),
            ("Verde Almacenes SL", "compras@verdealmacenes.es", CustomerTier.Silver, "Valencia", "ES"),
            ("Kroon Opslag BV", "inkoop@kroonopslag.nl", CustomerTier.Standard, "Rotterdam", "NL"),
            ("Baltic Supply OU", "info@balticsupply.ee", CustomerTier.Standard, "Tallinn", "EE"),
            ("Alpine Depot AG", "bestellung@alpinedepot.ch", CustomerTier.Standard, "Zurich", "CH"),
        ];

        List<Customer> customers = [];

        foreach ((string company, string emailText, CustomerTier tier, string city, string country) in accounts)
        {
            EmailAddress email = EmailAddress.Create(emailText).Value;

            Address address = Address
                .Create("Via Industriale 42", null, city, null, "20100", country)
                .Value;

            customers.Add(Customer.Create(company, email, address, tier).Value);
        }

        return customers;
    }

    private static List<Warehouse> SeedWarehouses(List<Product> products)
    {
        (string Code, string Name, string City, string Country)[] sites =
        [
            ("MIL-01", "Milano Distribution Centre", "Milano", "IT"),
            ("HAM-01", "Hamburg Hub", "Hamburg", "DE"),
            ("BCN-01", "Barcelona Depot", "Barcelona", "ES"),
        ];

        List<Warehouse> warehouses = [];

        // A deterministic seed, so every developer and every CI run gets the same numbers.
        // Random with no seed would make a failing test unreproducible - the worst kind of
        // failing test.
        var random = new Random(Seed: 20260101);

        foreach ((string code, string name, string city, string country) in sites)
        {
            Address address = Address
                .Create("Zona Industriale 1", null, city, null, "20090", country)
                .Value;

            Warehouse warehouse = Warehouse.Create(code, name, address).Value;

            foreach (Product product in products)
            {
                // Varied stock levels, including some deliberately at or below the reorder
                // threshold so the low-stock report has something to show on a fresh database.
                int onHand = random.Next(0, 400);
                int threshold = random.Next(20, 60);

                warehouse.AddStockItem(product.Id, onHand, threshold);
            }

            warehouses.Add(warehouse);
        }

        return warehouses;
    }
}
