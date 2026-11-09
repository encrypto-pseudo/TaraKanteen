TaraKanteen - ASP.NET Core + SQLite

Project layout:
  Program.cs          ASP.NET Core API and SQLite setup
  app.db              SQLite database (Users table)
  wwwroot/index.html
  wwwroot/login.html
  wwwroot/register.html
  wwwroot/student.html  Student menu, ordering, and order history
  wwwroot/admin.html    Admin inventory and sales dashboard
  wwwroot/dashboard.js  Role dashboards and API interactions

IMPORTANT:
- The uploaded HTML files reference style.css, but style.css was not included in the uploaded files.
  Copy your existing style.css into wwwroot/style.css to preserve the design.
- app.db is stored in the project root, outside wwwroot, so it is not served publicly.
- Do not host this project by double-clicking the HTML files. Run the ASP.NET Core server.

Run on Windows:
1. Install the .NET 8 SDK.
2. Open PowerShell in this folder.
3. Run:
     dotnet restore
     dotnet run
4. Open the local URL printed by dotnet run (usually https://localhost:xxxx or http://localhost:xxxx).
5. Open /register.html, create a student account, then use /login.html. Successful login sends each role to its dashboard.

To grant administrator access, first create and log in to the account, stop the app, then update that account's Role in the SQLite Users table:
  UPDATE Users SET Role = 'Admin' WHERE Username = 'your-admin-username';
Restart the app and log in again. Public registration always creates a student account.

How it works:
- register.html POSTs JSON to /api/register.
- Program.cs hashes passwords and stores them in the Users table in app.db.
- login.html POSTs JSON to /api/login.
- Successful login creates an HTTP-only authentication cookie.
- student.html lists available products, checks current prices and stock, places orders, and shows order history.
- admin.html creates and updates products, manages stock and availability, records walk-in sales, updates order status, and monitors sales transactions.
- Product and order data are stored in Products, Orders, and OrderItems tables. Placing an order records the sale and deducts inventory; cancelling an order restores its stock.

The existing app.db is included. Program.cs uses EnsureCreatedAsync for a new database and creates
the product and order tables if they are missing from an existing database. Back up app.db before
changing database structure.
