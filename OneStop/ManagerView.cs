using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using static OneStop.DBConnect;


namespace OneStop
{
    public partial class ManagerView : Form
    {

        public string LoggedInUsername { get; set; }

        // Manager state
        private string currentCategory = "All";
        private DiscountInfo appliedDiscount;
        private decimal appliedDiscountAmount;

        private int _currentCustomerId = 0;
        private string _currentCustomerName = "";
        private DataTable _editBuffer;
        private object _originalCellValue = null;
        private DataTable _invBuffer;
        private DataTable _discBuffer;
        private DataTable _ordersBuffer;
        private DataTable _orderDetailsBuffer;
        private string _selectedImagePath = null;

        


        public ManagerView()
        {
            InitializeComponent();

            

            // Make Enter behave like Tab
            dgvInventory.StandardTab = true;
            dgvDisc.StandardTab = true;

            btnCancel.CausesValidation = false;


            LoggedInUsername = null;

            flpManProducts.WrapContents = true;
            flpManProducts.AutoScroll = true;
            flpManProducts.FlowDirection = FlowDirection.LeftToRight;

            flpOrderSummary.AutoScroll = true;
            flpOrderSummary.WrapContents = false;
            flpOrderSummary.FlowDirection = FlowDirection.TopDown;

            dgvReports.ReadOnly = true;
            dgvReports.AllowUserToAddRows = false;
            dgvReports.AllowUserToDeleteRows = false;
            dgvReports.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvReports.MultiSelect = false;

            // Load AFTER the form is displayed to avoid layout issues
            this.Shown -= ManagerView_Shown;
            this.Shown += ManagerView_Shown;

            DBConnect.QuestionsBox(cbxQuestions1, cbxQuestions2, cbxQuestions3);

            cbxTitle.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxSuffix.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxQuestions1.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxQuestions2.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxQuestions3.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxPosition.DropDownStyle = ComboBoxStyle.DropDownList;

            btnClearDsicounts.CausesValidation = false;


        }

        private void ManagerView_Shown(object sender, EventArgs e)
        {
            // 1) Customers grid (if not already bound)
            InitCustomersGrid();
            BindCustomersGrid();

            InitInventoryGrid();
            BindInventoryGrid("");

            BindDiscountGrid();

            BindOrdersGrid();

            // 2) Load ALL products initially
            LoadProductsIntoFlow(null);

            // Status
            lblCurrentCustomer.Text = "No customer selected";




        }



        private void SafeEndEdits(DataGridView grid, DataTable buffer)
        {
            try { grid.EndEdit(); } catch { }
            try { grid.CommitEdit(DataGridViewDataErrorContexts.Commit); } catch { }
            try { grid.CancelEdit(); } catch { }

            try
            {
                if (buffer != null)
                {
                    var cm = (CurrencyManager)this.BindingContext[buffer];
                    cm?.EndCurrentEdit();
                    cm?.CancelCurrentEdit(); // if End fails, this clears the edit
                }
            }
            catch { }

            try { grid.CurrentCell = null; } catch { }
        }







        private void btnSearchCust_Click(object sender, EventArgs e)
        {
            dgvCustomers.AutoGenerateColumns = true;
            dgvCustomers.DataSource = DBConnect.SearchCustomers(tbxSearch.Text.Trim());
        }

        private void btnFindProduct_Click(object sender, EventArgs e)
        {
            try
            {
                string term = tbxSearchProduct.Text?.Trim() ?? string.Empty;

                flpManProducts.SuspendLayout();
                flpManProducts.Controls.Clear();

                DataTable dt = DBConnect.SearchInventoryProducts(term);
                if (dt == null || dt.Rows.Count == 0)
                {
                    flpManProducts.Controls.Add(new Label
                    {
                        Text = "No products found.",
                        AutoSize = true,
                        Margin = new Padding(8)
                    });
                    return;
                }

                foreach (DataRow r in dt.Rows)
                {
                    int inventoryId = Convert.ToInt32(r["InventoryID"]);
                    string name = Convert.ToString(r["ItemName"]);
                    decimal retail = Convert.ToDecimal(r["RetailPrice"]); 
                    int stock = Convert.ToInt32(r["Quantity"]);

                    string desc = dt.Columns.Contains("ItemDescription") && r["ItemDescription"] != DBNull.Value
                        ? Convert.ToString(r["ItemDescription"])
                        : null;

                    byte[] imageBytes = (dt.Columns.Contains("ItemImage") && r["ItemImage"] != DBNull.Value)
                        ? (byte[])r["ItemImage"]
                        : null;

                    // Use the same builder used by LoadProductsIntoFlow
                    var card = BuildProductCard(inventoryId, name, retail, stock, desc, imageBytes);
                    flpManProducts.Controls.Add(card);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading products:\n" + ex.Message,
                    "Product Search", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                flpManProducts.ResumeLayout();
            }

        }

        private void tbxSearchProduct_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                LoadProductsIntoFlow(tbxSearchProduct.Text.Trim());
            }
        }

        private void ProductCard_ProductSelected(ItemData item)
        {
            // 1) If the item is already in the cart, just increment its qty
            Panel FindCartPanelByInventoryId(int inventoryId)
            {
                foreach (Panel p in flpOrderSummary.Controls.OfType<Panel>())
                    if (p.Tag is Validation.OrderLine ol && ol.InventoryID == inventoryId)
                        return p;
                return null;
            }

            var existing = FindCartPanelByInventoryId(item.InventoryID);
            if (existing != null)
            {
                var qtyBoxExisting = existing.Controls.OfType<TextBox>().FirstOrDefault();
                if (qtyBoxExisting != null)
                {
                    if (!int.TryParse(qtyBoxExisting.Text.Trim(), out int cur)) cur = 0;
                    int newQty = cur + 1;

                    // Optional: clamp to current stock
                    try
                    {
                        using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                        {
                            conn.Open();
                            using (var tx = conn.BeginTransaction())
                            {
                                int stock = DBConnect.GetInventoryStock(item.InventoryID, conn, tx);
                                tx.Commit();
                                if (newQty > stock) newQty = stock;
                            }
                        }
                    }
                    catch { /* ignore stock check errors */ }

                    qtyBoxExisting.Text = newQty.ToString(); // triggers its TextChanged
                    flpOrderSummary.ScrollControlIntoView(existing);
                    UpdateTotal();
                    return;
                }
            }

            // 2) Create a new cart line
            var panel = new Panel
            {
                Width = 400,
                Height = 100,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(5),
                Tag = new Validation.OrderLine
                {
                    InventoryID = item.InventoryID,
                    ItemName = item.Name,
                    PriceEach = item.Price,
                    Qty = 1
                }
            };

            var nameLabel = new Label { Text = "Name: " + item.Name, AutoSize = true, Location = new Point(10, 10) };
            var priceLabel = new Label { Text = "Cost: $" + item.Price.ToString("F2"), AutoSize = true, Location = new Point(10, 30) };
            var qtyLabel = new Label { Text = "Qty:", AutoSize = true, Location = new Point(10, 55) };
            var qtyBox = new TextBox { Text = "1", Width = 40, Location = new Point(50, 52) };

            // digits only
            qtyBox.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                    e.Handled = true;
            };

            // qty change behavior: remove on 0, clamp to stock, update OrderLine + totals
            qtyBox.TextChanged += (s, e) =>
            {
                if (!(panel.Tag is Validation.OrderLine ol)) { UpdateTotal(); return; }

                if (!int.TryParse(qtyBox.Text.Trim(), out int q)) q = 0;

                if (q <= 0)
                {
                    // remove safely after event returns
                    this.BeginInvoke(new Action(() =>
                    {
                        if (flpOrderSummary.Controls.Contains(panel))
                        {
                            flpOrderSummary.Controls.Remove(panel);
                            panel.Dispose();
                        }
                        UpdateTotal();
                    }));
                    return;
                }

                // Clamp to live stock; if stock==0, remove the line
                try
                {
                    using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                    {
                        conn.Open();
                        using (var tx = conn.BeginTransaction())
                        {
                            int stock = DBConnect.GetInventoryStock(ol.InventoryID, conn, tx);
                            tx.Commit();

                            if (stock <= 0)
                            {
                                this.BeginInvoke(new Action(() =>
                                {
                                    if (flpOrderSummary.Controls.Contains(panel))
                                    {
                                        flpOrderSummary.Controls.Remove(panel);
                                        panel.Dispose();
                                    }
                                    UpdateTotal();
                                }));
                                return;
                            }

                            if (q > stock)
                            {
                                MessageBox.Show($"Only {stock} left in stock. Quantity adjusted.",
                                    "Stock Limited", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                qtyBox.Text = stock.ToString(); // will re-enter this handler
                                return;
                            }
                        }
                    }
                }
                catch { /* ignore stock check errors and proceed */ }

                // persist qty and refresh totals
                ol.Qty = q;
                UpdateTotal();
            };

            panel.Controls.Add(nameLabel);
            panel.Controls.Add(priceLabel);
            panel.Controls.Add(qtyLabel);
            panel.Controls.Add(qtyBox);

            flpOrderSummary.Controls.Add(panel);
            flpOrderSummary.ScrollControlIntoView(panel);
            UpdateTotal();
        }


        private void btnPosCheckout_Click(object sender, EventArgs e)
        {
            if (!Validation.IsManagerLoggedIn() && !Validation.IsEmployeeLoggedIn())
            {
                MessageBox.Show("Please log in as a manager/employee to complete the purchase.",
                                "Login Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // A customer must be selected
            if (!Validation.IsManagerCustomerSelected())
            {
                MessageBox.Show("Please select a customer before checking out.",
                                "Customer Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Cart must have items
            if (!Validation.IsCartNotEmpty(flpOrderSummary, out string cartError))
            {
                MessageBox.Show(cartError, "Checkout Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ❗ Read RAW digits from masked inputs
            mtbCardInfo.TextMaskFormat = MaskFormat.ExcludePromptAndLiterals;
            mtbCCV.TextMaskFormat = MaskFormat.ExcludePromptAndLiterals;
            mtbExpDate.TextMaskFormat = MaskFormat.ExcludePromptAndLiterals;

            string cardNumberRaw = (mtbCardInfo.Text ?? string.Empty).Trim(); // 13–19 digits supported by your validator
            string ccvRaw = (mtbCCV.Text ?? string.Empty).Trim();      // 3–4 digits
            string expRaw = (mtbExpDate.Text ?? string.Empty).Trim();  // "MMYY" or "MMYYYY" depending on your mask

            // Call your existing validator AS-IS
            if (!Validation.ValidateCheckoutFields(cardNumberRaw, ccvRaw, expRaw, out string error))
            {
                MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ✅ Perform checkout operations in database (your existing method)
            bool success = DBConnect.CheckoutAndInsertOrder(mtbCardInfo, mtbCCV, mtbExpDate, tbxAddDiscount, flpOrderSummary);

            if (success)
            {
                // Refresh totals before clearing
                UpdateTotal();

                GenerateHtmlReceipt_Pos();

                // Reset UI for next order
                flpOrderSummary.Controls.Clear();
                mtbCardInfo.Clear();
                mtbCCV.Clear();
                mtbExpDate.Clear();
                tbxAddDiscount.Text = "";
                appliedDiscount = null;
                appliedDiscountAmount = 0m;

                if (lblSubtotal != null) lblSubtotal.Text = "0.00";
                if (lblDiscount != null) lblDiscount.Text = "0.00";
                if (lblTax != null) lblTax.Text = "0.00";
                if (lblTotal != null) lblTotal.Text = "0.00";

                MessageBox.Show("Checkout complete.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Checkout failed. Please try again.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GenerateHtmlReceipt_Pos()
        {
            // ---- Resolve Customer Full Name inline via existing DBConnect methods ----
            string customerDisplay = "(Unknown)";
            try
            {
                // Prefer exact PersonID match
                if (Validation.CurrentManagerSelectedCustomerId.HasValue)
                {
                    var all = DBConnect.GetAllCustomers(); // Columns: PersonID, LogonName, FullName, Email, PhonePrimary
                    var rows = all.Select("PersonID = " + Validation.CurrentManagerSelectedCustomerId.Value);
                    if (rows != null && rows.Length > 0)
                        customerDisplay = rows[0]["FullName"]?.ToString();
                }

                // Fallback to exact LogonName match (or first LIKE match)
                if (string.IsNullOrWhiteSpace(customerDisplay) || customerDisplay == "(Unknown)")
                {
                    var ln = Validation.CurrentManagerSelectedCustomerLogonName;
                    if (!string.IsNullOrWhiteSpace(ln))
                    {
                        var dt = DBConnect.SearchCustomers(ln);
                        if (dt != null && dt.Rows.Count > 0)
                        {
                            string escaped = ln.Replace("'", "''");
                            var exact = dt.Select($"LogonName = '{escaped}'");
                            var row = (exact != null && exact.Length > 0) ? exact[0] : dt.Rows[0];
                            customerDisplay = row["FullName"]?.ToString() ?? "(Unknown)";
                        }
                    }
                }
            }
            catch
            {
                customerDisplay = "(Unknown)";
            }

            // Staff display (still usernames unless you add similar employee methods)
            string handledBy =
                !string.IsNullOrWhiteSpace(Validation.LoggedInManagerUsername)
                    ? Validation.LoggedInManagerUsername
                    : Validation.LoggedInEmployeeUsername ?? "(Unknown)";

            string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            // ---- Helper: extract RETAIL price from fallback labels (never use "Cost:") ----
            decimal ExtractUnitPrice(Panel p)
            {
                string[] prefixes = { "Retail Price:", "Retail:", "Unit Price:", "Unit:", "Price:" };
                foreach (var prefix in prefixes)
                {
                    var lbl = p.Controls.OfType<Label>()
                        .FirstOrDefault(l => l.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    if (lbl != null)
                    {
                        string raw = lbl.Text.Substring(prefix.Length).Replace("$", "").Trim();
                        if (decimal.TryParse(raw, out var val)) return val;
                    }
                }
                return 0m;
            }

            // ---- Subtotal (prefer OrderLine info; fallback to retail labels) ----
            decimal subtotal = 0m;
            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
            {
                if (panel.Tag is Validation.OrderLine ol)
                {
                    subtotal += ol.LineTotal; // RetailPrice * Qty expected
                }
                else
                {
                    decimal unit = ExtractUnitPrice(panel);
                    var qtyT = panel.Controls.OfType<TextBox>().FirstOrDefault();
                    int qty = 0;
                    if (qtyT != null) int.TryParse(qtyT.Text.Trim(), out qty);
                    subtotal += unit * qty;
                }
            }

            // ---- Discount (percent stored as decimal fraction, e.g., 0.20 = 20%) ----
            decimal discountAmount = 0m;
            if (appliedDiscount != null)
            {
                if (appliedDiscount.DiscountLevel == 0)
                {
                    if (appliedDiscount.DiscountType == 0 && appliedDiscount.DiscountPercentage.HasValue)
                        discountAmount = Math.Round(subtotal * appliedDiscount.DiscountPercentage.Value, 2);
                    else if (appliedDiscount.DiscountType == 1 && appliedDiscount.DiscountDollarAmount.HasValue)
                        discountAmount = appliedDiscount.DiscountDollarAmount.Value;
                }
                else if (appliedDiscount.DiscountLevel == 1 && appliedDiscount.InventoryID.HasValue)
                {
                    foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                    {
                        int qty = 0; decimal price = 0m; int? invId = null;

                        if (panel.Tag is Validation.OrderLine ol)
                        {
                            qty = ol.Qty;
                            price = ol.PriceEach; // Retail
                            invId = ol.InventoryID;
                        }
                        else
                        {
                            price = ExtractUnitPrice(panel);
                            var qtyT = panel.Controls.OfType<TextBox>().FirstOrDefault();
                            if (qtyT != null) int.TryParse(qtyT.Text.Trim(), out qty);

                            var nameL = panel.Controls.OfType<Label>()
                                .FirstOrDefault(l => l.Text.StartsWith("Name:", StringComparison.OrdinalIgnoreCase));
                            if (nameL != null)
                            {
                                string n = nameL.Text.Replace("Name: ", "").Trim();
                                using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                                {
                                    conn.Open();
                                    using (var tx = conn.BeginTransaction())
                                    {
                                        invId = DBConnect.GetInventoryIDByName(n, conn, tx);
                                        tx.Commit();
                                    }
                                }
                            }
                        }

                        if (invId.HasValue && invId.Value == appliedDiscount.InventoryID.Value)
                        {
                            if (appliedDiscount.DiscountType == 0 && appliedDiscount.DiscountPercentage.HasValue)
                                discountAmount += Math.Round((price * qty) * appliedDiscount.DiscountPercentage.Value, 2);
                            else if (appliedDiscount.DiscountType == 1 && appliedDiscount.DiscountDollarAmount.HasValue)
                                discountAmount += appliedDiscount.DiscountDollarAmount.Value * qty;
                        }
                    }
                }

                if (discountAmount < 0) discountAmount = 0m;
                if (discountAmount > subtotal) discountAmount = subtotal;
            }

            decimal discountedSubtotal = subtotal - discountAmount;
            decimal tax = Math.Round(discountedSubtotal * 0.0825m, 2);
            decimal total = discountedSubtotal + tax;

            // ---- Build HTML ----
            var html = new System.Text.StringBuilder();
            html.AppendLine("<html><head><title>POS Receipt</title></head><body>");
            html.AppendLine("<h1>POS Receipt</h1>");
            html.AppendLine($"<p><strong>Customer:</strong> {customerDisplay}</p>");
            html.AppendLine($"<p><strong>Handled By:</strong> {handledBy}</p>");
            html.AppendLine($"<p><strong>Date:</strong> {date}</p>");
            html.AppendLine("<table border='1' cellpadding='5'><tr><th>Item</th><th>Qty</th><th>Unit</th><th>Line</th></tr>");

            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
            {
                string name = "";
                decimal unit = 0m; int qty = 0; decimal line = 0m;

                if (panel.Tag is Validation.OrderLine ol)
                {
                    name = ol.ItemName;
                    unit = ol.PriceEach; // Retail
                    qty = ol.Qty;
                    line = ol.LineTotal;
                }
                else
                {
                    var nameL = panel.Controls.OfType<Label>()
                        .FirstOrDefault(l => l.Text.StartsWith("Name:", StringComparison.OrdinalIgnoreCase));
                    if (nameL != null) name = nameL.Text.Replace("Name: ", "").Trim();

                    unit = ExtractUnitPrice(panel);
                    var qtyT = panel.Controls.OfType<TextBox>().FirstOrDefault();
                    if (qtyT != null) int.TryParse(qtyT.Text.Trim(), out qty);

                    line = unit * qty;
                }

                html.AppendLine($"<tr><td>{name}</td><td>{qty}</td><td>${unit:F2}</td><td>${line:F2}</td></tr>");
            }
            html.AppendLine("</table>");
            html.AppendLine($"<p><strong>Subtotal:</strong> ${subtotal:F2}</p>");
            html.AppendLine($"<p><strong>Discount:</strong> -${discountAmount:F2}</p>");
            html.AppendLine($"<p><strong>Tax (8.25%):</strong> ${tax:F2}</p>");
            html.AppendLine($"<h3>Total: ${total:F2}</h3>");
            html.AppendLine("</body></html>");

            // ---- Save & open ----
            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string receiptFolder = System.IO.Path.Combine(documents, "OneStopReceipts");
                Directory.CreateDirectory(receiptFolder);
                string file = System.IO.Path.Combine(receiptFolder, $"POS_Receipt_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                File.WriteAllText(file, html.ToString());
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error generating or opening receipt:\n" + ex.Message);
            }
        }



        private void dgvCustomers_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (dgvCustomers.SelectedRows.Count == 0) return;

            var row = dgvCustomers.SelectedRows[0];

            // read values from cells (adjust names if your columns differ)
            object pidObj = row.Cells["PersonID"].Value;
            object logonObj = row.Cells["LogonName"].Value;

            if (pidObj != null && int.TryParse(pidObj.ToString(), out int pid))
                Validation.CurrentManagerSelectedCustomerId = pid;
            else
                Validation.CurrentManagerSelectedCustomerId = null;

            Validation.CurrentManagerSelectedCustomerLogonName = logonObj?.ToString();

            // Optional: show selected customer name on UI
            var fullName = row.Cells["FullName"]?.Value?.ToString();
            lblCurrentCustomer.Text = string.IsNullOrWhiteSpace(fullName)
                ? "Current Customer: (none)"
                : $"Current Customer: {fullName}";

        }





        private void UpdateTotal()
        {
            decimal subtotal = 0m;
            decimal discountAmount = 0m;

            foreach (var label in flpOrderSummary.Controls.OfType<Label>()
                             .Where(l => (l.Tag as string) == "Totals").ToList())
            {
                flpOrderSummary.Controls.Remove(label);
                label.Dispose();
            }

            // Local helper: read retail unit price from labels if we don't have an OrderLine tag
            decimal UnitFromLabels(Panel p)
            {
                string[] prefixes = { "Retail Price:", "Retail:", "Unit Price:", "Unit:", "Price:" };
                foreach (var prefix in prefixes)
                {
                    var lbl = p.Controls.OfType<Label>()
                        .FirstOrDefault(l => l.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    if (lbl != null)
                    {
                        var raw = lbl.Text.Substring(prefix.Length).Replace("$", "").Trim();
                        if (decimal.TryParse(raw, out var val)) return val;
                    }
                }
                return 0m;
            }

            // ---- Subtotal (prefer OrderLine; fallback to retail labels) ----
            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
            {
                if (panel.Tag is Validation.OrderLine ol)
                {
                    subtotal += ol.LineTotal;          // ✅ Retail * Qty stored in Tag
                }
                else
                {
                    var unit = UnitFromLabels(panel);  // ✅ read RETAIL label
                    var qtyT = panel.Controls.OfType<TextBox>().FirstOrDefault();
                    int qty = 0; if (qtyT != null) int.TryParse(qtyT.Text.Trim(), out qty);
                    subtotal += unit * qty;
                }
            }

            // ---- Discount calculation (unchanged logic, but ensure price source is retail) ----
            if (appliedDiscount != null)
            {
                try
                {
                    using (SQLiteConnection conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                    {
                        conn.Open();
                        using (SQLiteTransaction tx = conn.BeginTransaction())
                        {
                            if (appliedDiscount.DiscountLevel == 0)
                            {
                                if (appliedDiscount.DiscountType == 0 && appliedDiscount.DiscountPercentage.HasValue)
                                    discountAmount = Math.Round(subtotal * appliedDiscount.DiscountPercentage.Value, 2);
                                else if (appliedDiscount.DiscountType == 1 && appliedDiscount.DiscountDollarAmount.HasValue)
                                    discountAmount = appliedDiscount.DiscountDollarAmount.Value;
                            }
                            else if (appliedDiscount.DiscountLevel == 1)
                            {
                                foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                                {
                                    int qty = 0; decimal price = 0m; int? itemInvId = null;

                                    if (panel.Tag is Validation.OrderLine ol)
                                    {
                                        qty = ol.Qty;
                                        price = ol.PriceEach;      // ✅ retail
                                        itemInvId = ol.InventoryID;
                                    }
                                    else
                                    {
                                        price = UnitFromLabels(panel); // ✅ retail from label
                                        var qtyT = panel.Controls.OfType<TextBox>().FirstOrDefault();
                                        if (qtyT != null) int.TryParse(qtyT.Text.Trim(), out qty);

                                        var nameLabel = panel.Controls.OfType<Label>()
                                            .FirstOrDefault(l => l.Text.StartsWith("Name:", StringComparison.OrdinalIgnoreCase));
                                        if (nameLabel != null)
                                        {
                                            string itemName = nameLabel.Text.Replace("Name: ", "").Trim();
                                            itemInvId = DBConnect.GetInventoryIDByName(itemName, conn, tx);
                                        }
                                    }

                                    if (appliedDiscount.InventoryID.HasValue && itemInvId.HasValue &&
                                        appliedDiscount.InventoryID.Value == itemInvId.Value)
                                    {
                                        if (appliedDiscount.DiscountType == 0 && appliedDiscount.DiscountPercentage.HasValue)
                                            discountAmount += Math.Round((price * qty) * appliedDiscount.DiscountPercentage.Value, 2);
                                        else if (appliedDiscount.DiscountType == 1 && appliedDiscount.DiscountDollarAmount.HasValue)
                                            discountAmount += appliedDiscount.DiscountDollarAmount.Value * qty;
                                    }
                                }
                            }

                            tx.Commit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error calculating discount: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // Clear discount if no longer applicable (unchanged)
            if (appliedDiscount != null)
            {
                bool hasLines = flpOrderSummary.Controls.OfType<Panel>().Any();
                bool clearDiscount = false;

                if (appliedDiscount.DiscountLevel == 0) clearDiscount = !hasLines;
                else
                {
                    int targetInvId = appliedDiscount.InventoryID ?? -1;
                    bool anyLeft = flpOrderSummary.Controls.OfType<Panel>()
                                   .Any(p => p.Tag is Validation.OrderLine ol && ol.InventoryID == targetInvId && ol.Qty > 0);
                    clearDiscount = !anyLeft;
                }

                if (clearDiscount)
                {
                    var discLbl = flpOrderSummary.Controls.OfType<Label>().FirstOrDefault(l => l.Name == "lblDiscountApplied");
                    if (discLbl != null) { flpOrderSummary.Controls.Remove(discLbl); discLbl.Dispose(); }
                    appliedDiscount = null; appliedDiscountAmount = 0m;
                }
            }

            if (discountAmount < 0) discountAmount = 0m;
            if (discountAmount > subtotal) discountAmount = subtotal;

            decimal discountedSubtotal = subtotal - discountAmount;
            decimal taxAmount = Math.Round(discountedSubtotal * 0.0825m, 2);
            decimal finalTotal = Math.Round(discountedSubtotal + taxAmount, 2);

            if (flpOrderSummary.Controls.OfType<Panel>().Any())
            {
                if (lblSubtotal != null) lblSubtotal.Text = subtotal.ToString("F2");
                if (lblDiscount != null) lblDiscount.Text = discountAmount.ToString("F2");
                if (lblTax != null) lblTax.Text = taxAmount.ToString("F2");
                if (lblTotal != null) lblTotal.Text = finalTotal.ToString("F2");
            }
            else
            {
                if (lblSubtotal != null) lblSubtotal.Text = "0.00";
                if (lblDiscount != null) lblDiscount.Text = "0.00";
                if (lblTax != null) lblTax.Text = "0.00";
                if (lblTotal != null) lblTotal.Text = "0.00";
            }
        }




        private bool RemoveLineIfOutOfStock(Panel linePanel, int inventoryId)
        {
            try
            {
                using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        int currentStock = DBConnect.GetInventoryStock(inventoryId, conn, tx);
                        tx.Commit();

                        if (currentStock <= 0)
                        {
                            if (flpOrderSummary.Controls.Contains(linePanel))
                            {
                                flpOrderSummary.Controls.Remove(linePanel);
                                linePanel.Dispose();
                            }

                            // 🔹 If a discount is applied, clear it when appropriate
                            if (appliedDiscount != null)
                            {
                                bool shouldClear =
                                    // Cart-level discount applies to the whole cart
                                    (appliedDiscount.DiscountLevel == 0)
                                    ||
                                    // Item-level discount tied to this inventory item
                                    (appliedDiscount.DiscountLevel == 1 &&
                                     appliedDiscount.InventoryID.HasValue &&
                                     appliedDiscount.InventoryID.Value == inventoryId);

                                if (shouldClear)
                                {
                                    var discLbl = flpOrderSummary.Controls
                                                    .OfType<Label>()
                                                    .FirstOrDefault(l => l.Name == "lblDiscountApplied");

                                    if (discLbl != null)
                                    {
                                        flpOrderSummary.Controls.Remove(discLbl);
                                        discLbl.Dispose();
                                    }

                                    appliedDiscount = null;
                                    appliedDiscountAmount = 0m;
                                }
                            }


                            // Update all totals after removal
                            UpdateTotal();
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error checking stock: " + ex.Message, "Stock", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return false;
        }



        private void AddDiscountLabelToSummary(string code, decimal discountAmount)
        {
            Control existing = flpOrderSummary.Controls
                .OfType<Label>()
                .FirstOrDefault(l => l.Name == "lblDiscountApplied");

            if (existing != null)
                flpOrderSummary.Controls.Remove(existing);


            Label discountLabel = new Label
            {
                Name = "lblDiscountApplied",
                Text = $"Discount Code Applied ({code}): -${discountAmount:F2}",
                ForeColor = Color.Green,
                AutoSize = true,
                Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Italic),
                Margin = new Padding(5)
            };

            flpOrderSummary.Controls.Add(discountLabel);
        }

        private void btnAddDiscount_Click(object sender, EventArgs e)
        {
            string code = tbxAddDiscount.Text.Trim();

            // 1) Must have items in cart
            if (!Validation.IsCartNotEmpty(flpOrderSummary, out string cartError))
            {
                MessageBox.Show(cartError, "Discount Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2) Require code
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Please enter a discount code.", "Discount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3) Prevent re-applying same code (optional)
            if (appliedDiscount != null &&
                string.Equals(appliedDiscount.DiscountCode, code, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("This discount has already been applied.", "Discount",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // ---- local helpers (retail-first) ----
            decimal ExtractRetail(Panel p)
            {
                string[] prefixes = { "Retail Price:", "Retail:", "Unit Price:", "Unit:", "Price:" };
                foreach (var prefix in prefixes)
                {
                    var lbl = p.Controls.OfType<Label>()
                        .FirstOrDefault(l => l.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    if (lbl != null)
                    {
                        var raw = lbl.Text.Substring(prefix.Length).Replace("$", "").Trim();
                        if (decimal.TryParse(raw, out var val)) return val;
                    }
                }
                return 0m;
            }

            int ExtractQty(Panel p)
            {
                var qtyBox = p.Controls.OfType<TextBox>().FirstOrDefault();
                if (qtyBox != null && int.TryParse(qtyBox.Text.Trim(), out int q)) return q;
                return 0;
            }
            // --------------------------------------

            try
            {
                using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        // Look up discount (assumes this method checks active/valid)
                        DiscountInfo discount = DBConnect.GetDiscountInfoByCode(code);
                        if (discount == null)
                        {
                            MessageBox.Show("Invalid or inactive discount code.", "Discount",
                                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            tx.Rollback();
                            return;
                        }

                        // Calculate subtotal using RETAIL
                        decimal subtotal = 0m;
                        foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                        {
                            if (panel.Tag is Validation.OrderLine ol)
                            {
                                subtotal += ol.LineTotal; // Retail * Qty
                            }
                            else
                            {
                                decimal unit = ExtractRetail(panel);
                                int qty = ExtractQty(panel);
                                subtotal += unit * qty;
                            }
                        }

                        decimal discountAmount = 0m;

                        // Levels: 0 = cart-wide, 1 = item-specific
                        // Types:  0 = percent,   1 = flat dollar
                        if (discount.DiscountLevel == 0)
                        {
                            // Cart-level
                            if (discount.DiscountType == 0 && discount.DiscountPercentage.HasValue)
                            {
                                discountAmount = Math.Round(subtotal * discount.DiscountPercentage.Value, 2);
                            }
                            else if (discount.DiscountType == 1 && discount.DiscountDollarAmount.HasValue)
                            {
                                discountAmount = discount.DiscountDollarAmount.Value;
                            }
                            else
                            {
                                MessageBox.Show("Discount is not configured properly (cart level).",
                                                "Discount Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                tx.Rollback();
                                return;
                            }
                        }
                        else if (discount.DiscountLevel == 1)
                        {
                            if (!discount.InventoryID.HasValue)
                            {
                                MessageBox.Show("Discount is not configured properly (item level missing InventoryID).",
                                                "Discount Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                tx.Rollback();
                                return;
                            }

                            int targetInvId = discount.InventoryID.Value;
                            bool anyApplied = false;

                            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                            {
                                int qty = 0;
                                decimal price = 0m;
                                int? itemInventoryId = null;

                                if (panel.Tag is Validation.OrderLine ol)
                                {
                                    qty = ol.Qty;
                                    price = ol.PriceEach;     // ✅ Retail
                                    itemInventoryId = ol.InventoryID;
                                }
                                else
                                {
                                    price = ExtractRetail(panel); // ✅ Retail from label
                                    qty = ExtractQty(panel);

                                    // Resolve the InventoryID by the visible name if needed
                                    var nameLabel = panel.Controls.OfType<Label>()
                                        .FirstOrDefault(l => l.Text.StartsWith("Name:", StringComparison.OrdinalIgnoreCase));
                                    if (nameLabel != null)
                                    {
                                        string itemName = nameLabel.Text.Replace("Name: ", "").Trim();
                                        itemInventoryId = DBConnect.GetInventoryIDByName(itemName, conn, tx);
                                    }
                                }

                                if (itemInventoryId.HasValue && itemInventoryId.Value == targetInvId)
                                {
                                    anyApplied = true;

                                    if (discount.DiscountType == 0 && discount.DiscountPercentage.HasValue)
                                    {
                                        discountAmount += Math.Round((price * qty) * discount.DiscountPercentage.Value, 2);
                                    }
                                    else if (discount.DiscountType == 1 && discount.DiscountDollarAmount.HasValue)
                                    {
                                        discountAmount += discount.DiscountDollarAmount.Value * qty;
                                    }
                                    else
                                    {
                                        MessageBox.Show("Discount is not configured properly (item level).",
                                                        "Discount Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                        tx.Rollback();
                                        return;
                                    }
                                }
                            }

                            if (!anyApplied || discountAmount <= 0m)
                            {
                                MessageBox.Show("This discount does not apply to any items in your cart.",
                                                "Discount Not Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                tx.Rollback();
                                return;
                            }
                        }

                        // Cap to subtotal
                        if (discountAmount < 0) discountAmount = 0m;
                        if (discountAmount > subtotal) discountAmount = subtotal;

                        // Persist and update UI
                        appliedDiscount = discount;
                        appliedDiscountAmount = discountAmount;

                        AddDiscountLabelToSummary(code, discountAmount);
                        tx.Commit();

                        UpdateTotal();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error applying discount:\n" + ex.Message,
                                "Discount Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadProductsIntoFlow(string term)
        {
            flpManProducts.Controls.Clear();

            DataTable dt = DBConnect.GetAllInventoryProducts();

            foreach (DataRow r in dt.Rows)
            {
                int inventoryId = Convert.ToInt32(r["InventoryID"]);
                string name = r["ItemName"].ToString();
                decimal retail = Convert.ToDecimal(r["RetailPrice"]);
                int stock = Convert.ToInt32(r["Quantity"]);

                string desc = dt.Columns.Contains("ItemDescription") && r["ItemDescription"] != DBNull.Value
                    ? r["ItemDescription"].ToString()
                    : null;

                byte[] imageBytes = (dt.Columns.Contains("ItemImage") && r["ItemImage"] != DBNull.Value)
                    ? (byte[])r["ItemImage"]
                    : null;

                // Call the helper method
                var card = BuildProductCard(inventoryId, name, retail, stock, desc, imageBytes);

                flpManProducts.Controls.Add(card);
            }
        }

        private Control BuildProductCard(
     int inventoryId,
     string name,
     decimal retail,
     int stock,
     string description = null,
     byte[] imageBytes = null)
        {
            var card = new Panel
            {
                Width = 280,
                Height = 220,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(6),
                Tag = inventoryId
            };

            Image img = null;
            if (imageBytes != null && imageBytes.Length > 0)
            {
                try { using (var ms = new MemoryStream(imageBytes)) img = Image.FromStream(ms); } catch { }
            }

            var pb = new PictureBox
            {
                Width = 90,
                Height = 90,
                Location = new Point(10, 10),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = img
            };

            var lblName = new Label { Text = name, AutoSize = false, Width = 160, Height = 40, Location = new Point(110, 10), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            var lblPrice = new Label { Text = $"Retail Price: ${retail:F2}", AutoSize = true, Location = new Point(110, 55) };
            var lblStock = new Label { Text = $"Stock: {stock}", AutoSize = true, Location = new Point(110, 75) };

            string shortDesc = string.IsNullOrWhiteSpace(description) ? "" :
                               (description.Length > 140 ? description.Substring(0, 140) + "…" : description);

            var lblDesc = new Label { Text = shortDesc, AutoSize = false, Width = 260, Height = 60, Location = new Point(10, 110) };

            var btnAdd = new Button { Text = "Add", Width = 70, Height = 28, Location = new Point(200, 175) };
            btnAdd.Click += (s, e) =>
            {
                AddToOrderSummary(new ItemData
                {
                    InventoryID = inventoryId,
                    Name = name,
                    Price = retail,
                    Stock = stock,
                    Description = description,
                    ImageBytes = imageBytes
                });
            };

            card.Controls.Add(pb);
            card.Controls.Add(lblName);
            card.Controls.Add(lblPrice);
            card.Controls.Add(lblStock);
            card.Controls.Add(lblDesc);
            card.Controls.Add(btnAdd);
            return card;
        }


        private void AddToOrderSummary(ItemData item)
        {
            // 1) Merge if already in cart
            var existing = FindCartPanelByInventoryId(item.InventoryID);
            if (existing != null)
            {
                var qtyBoxExisting = existing.Controls.OfType<TextBox>().FirstOrDefault();
                if (qtyBoxExisting != null)
                {
                    if (!int.TryParse(qtyBoxExisting.Text.Trim(), out int cur)) cur = 0;
                    int newQty = cur + 1;

                    // Optional: clamp to stock
                    try
                    {
                        using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                        {
                            conn.Open();
                            using (var tx = conn.BeginTransaction())
                            {
                                int stock = DBConnect.GetInventoryStock(item.InventoryID, conn, tx);
                                tx.Commit();
                                if (newQty > stock) newQty = stock;
                            }
                        }
                    }
                    catch { /* ignore */ }

                    qtyBoxExisting.Text = newQty.ToString();
                    flpOrderSummary.ScrollControlIntoView(existing);
                    UpdateTotal();
                    return;
                }
            }

            // 2) Create a new cart line
            var line = new Panel
            {
                Width = 400,
                Height = 100,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(5),
                Tag = new Validation.OrderLine
                {
                    InventoryID = item.InventoryID,
                    ItemName = item.Name,
                    PriceEach = item.Price,
                    Qty = 1
                }
            };

            var nameLabel = new Label { Text = "Name: " + item.Name, Location = new Point(10, 10), AutoSize = true };
            var priceLabel = new Label { Text = "Retail Price: $" + item.Price.ToString("F2"), Location = new Point(10, 30), AutoSize = true };
            var qtyLabel = new Label { Text = "Qty:", Location = new Point(10, 55), AutoSize = true };
            var qtyBox = new TextBox { Text = "1", Width = 40, Location = new Point(50, 52) };

            // Digits only
            qtyBox.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                    e.Handled = true;
            };

            // Remove/adjust on change
            qtyBox.TextChanged += (s, e) =>
            {
                if (!(line.Tag is Validation.OrderLine ol))
                {
                    UpdateTotal();
                    return;
                }

                // Parse qty (treat blank/non-numeric as 0)
                if (!int.TryParse(qtyBox.Text.Trim(), out int q)) q = 0;

                // If 0 or less, remove the line safely
                if (q <= 0)
                {
                    // Use BeginInvoke to avoid modifying controls during the TextChanged event stack
                    this.BeginInvoke(new Action(() =>
                    {
                        if (flpOrderSummary.Controls.Contains(line))
                        {
                            flpOrderSummary.Controls.Remove(line);
                            line.Dispose();
                        }
                        UpdateTotal();
                    }));
                    return;
                }

                // Clamp to live stock and handle out-of-stock
                try
                {
                    using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                    {
                        conn.Open();
                        using (var tx = conn.BeginTransaction())
                        {
                            int stock = DBConnect.GetInventoryStock(ol.InventoryID, conn, tx);

                            if (stock <= 0)
                            {
                                this.BeginInvoke(new Action(() =>
                                {
                                    if (flpOrderSummary.Controls.Contains(line))
                                    {
                                        flpOrderSummary.Controls.Remove(line);
                                        line.Dispose();
                                    }
                                    UpdateTotal();
                                }));
                                tx.Commit();
                                return;
                            }

                            if (q > stock)
                            {
                                MessageBox.Show($"Only {stock} left in stock. Quantity adjusted.", "Stock Limited",
                                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                // Setting text will retrigger TextChanged and re-run logic
                                qtyBox.Text = stock.ToString();
                                tx.Commit();
                                return;
                            }

                            tx.Commit();
                        }
                    }
                }
                catch
                {
                    // If stock check fails, just accept the qty and continue
                }

                // Persist qty to OrderLine and refresh totals
                ol.Qty = q;
                UpdateTotal();
            };

            line.Controls.Add(nameLabel);
            line.Controls.Add(priceLabel);
            line.Controls.Add(qtyLabel);
            line.Controls.Add(qtyBox);

            flpOrderSummary.Controls.Add(line);
            flpOrderSummary.ScrollControlIntoView(line);
            UpdateTotal();
        }









        private void ManagerView_Load(object sender, EventArgs e)
        {
            if (!Validation.IsManagerLoggedIn())
            {
                MessageBox.Show("Access denied. Please log in as a manager.",
                    "Authorization", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            InitCustomersGrid();
            BindCustomersGrid();

            // Load all products into flpManProducts
            LoadProductsIntoFlow("");

            lblCurrentCustomer.Text = "No customer selected";
        }

        public void SetCurrentCustomer(int personId, string customerDisplay)
        {
            Validation.CurrentManagerSelectedCustomerId = personId;
            lblCurrentCustomer.Text = $"Current Customer: {customerDisplay} (ID: {personId})";
        }

        private void InitCustomersGrid()
        {
            dgvCustomers.AutoGenerateColumns = true;

            // Read-only behavior
            dgvCustomers.ReadOnly = true;
            dgvCustomers.EditMode = DataGridViewEditMode.EditProgrammatically;
            dgvCustomers.AllowUserToAddRows = false;
            dgvCustomers.AllowUserToDeleteRows = false;

            // Selection behavior
            dgvCustomers.MultiSelect = false;
            dgvCustomers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            // Optional cosmetics
            dgvCustomers.RowHeadersVisible = false;
            dgvCustomers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            // Belt & suspenders: cancel any attempt to edit
            dgvCustomers.CellBeginEdit += (s, e) => { e.Cancel = true; };

            // After binding, make absolutely sure every column is read-only
            dgvCustomers.DataBindingComplete += (s, e) =>
            {
                foreach (DataGridViewColumn col in dgvCustomers.Columns)
                {
                    col.ReadOnly = true;
                    col.SortMode = DataGridViewColumnSortMode.Automatic; // or NotSortable, your choice
                }

                if (dgvCustomers.Columns.Contains("PersonID"))
                    dgvCustomers.Columns["PersonID"].Visible = false;
                if (dgvCustomers.Columns.Contains("LogonName"))
                    dgvCustomers.Columns["LogonName"].HeaderText = "Logon Name";
            };

            // Keep your existing handlers
            dgvCustomers.SelectionChanged -= dgvCustomers_SelectionChanged;
            dgvCustomers.SelectionChanged += dgvCustomers_SelectionChanged;

            dgvCustomers.CellDoubleClick -= dgvCustomers_CellDoubleClick;
            dgvCustomers.CellDoubleClick += dgvCustomers_CellDoubleClick;
        }


        private void dgvCustomers_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            if (dgvCustomers.Columns.Contains("PersonID"))
                dgvCustomers.Columns["PersonID"].Visible = false;
            if (dgvCustomers.Columns.Contains("LogonName"))
                dgvCustomers.Columns["LogonName"].HeaderText = "Logon Name";

            // Select first visible row to avoid "current cell invisible" issues
            if (dgvCustomers.Rows.Count > 0)
            {
                foreach (DataGridViewColumn col in dgvCustomers.Columns)
                {
                    if (col.Visible)
                    {
                        dgvCustomers.CurrentCell = dgvCustomers.Rows[0].Cells[col.Index];
                        dgvCustomers.Rows[0].Selected = true;
                        break;
                    }
                }
            }
        }

        private void BindCustomersGrid()
        {
            // Get data
            var dt = DBConnect.GetAllCustomers();

            // Bind
            dgvCustomers.AutoGenerateColumns = true;
            dgvCustomers.DataSource = dt;

            // Lock the grid (selection only)
            dgvCustomers.ReadOnly = true;
            dgvCustomers.EditMode = DataGridViewEditMode.EditProgrammatically;
            dgvCustomers.AllowUserToAddRows = false;
            dgvCustomers.AllowUserToDeleteRows = false;
            dgvCustomers.MultiSelect = false;
            dgvCustomers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            // Hide internal ID, set friendly header if present
            if (dgvCustomers.Columns.Contains("PersonID"))
                dgvCustomers.Columns["PersonID"].Visible = false;

            if (dgvCustomers.Columns.Contains("LogonName"))
                dgvCustomers.Columns["LogonName"].HeaderText = "Logon Name";

            // Belt & suspenders: ensure every column is read-only
            foreach (DataGridViewColumn col in dgvCustomers.Columns)
                col.ReadOnly = true;

            // Extra safety: cancel any attempt to enter edit mode
            dgvCustomers.CellBeginEdit -= DgvCustomers_CellBeginEdit_Block;
            dgvCustomers.CellBeginEdit += DgvCustomers_CellBeginEdit_Block;
        }

        private void DgvCustomers_CellBeginEdit_Block(object sender, DataGridViewCellCancelEventArgs e)
        {
            e.Cancel = true; // never allow editing in this grid
        }


        private void InitInventoryGrid()
        {
            dgvInventory.AutoGenerateColumns = true;
            dgvInventory.AllowUserToAddRows = true;      // allow adding items
            dgvInventory.AllowUserToDeleteRows = false;
            dgvInventory.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            dgvInventory.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvInventory.MultiSelect = false;

            dgvInventory.DataBindingComplete -= dgvInventory_DataBindingComplete;
            dgvInventory.DataBindingComplete += dgvInventory_DataBindingComplete;

            dgvInventory.CellValidating -= dgvInventory_CellValidating;
            dgvInventory.CellValidating += dgvInventory_CellValidating;

            dgvInventory.CellValueChanged -= dgvInventory_CellValueChanged;
            dgvInventory.CellValueChanged += dgvInventory_CellValueChanged;

            dgvInventory.CurrentCellDirtyStateChanged -= dgvInventory_CurrentCellDirtyStateChanged;
            dgvInventory.CurrentCellDirtyStateChanged += dgvInventory_CurrentCellDirtyStateChanged;

            dgvInventory.DataError -= dgv_DataError_Soft;
            dgvInventory.DataError += dgv_DataError_Soft;
        }

        private void BindInventoryGrid(string term)
        {
            SafeEndEdits(dgvInventory, _invBuffer);
            _invBuffer = DBConnect.GetInventoryForEdit(term);
            dgvInventory.DataSource = _invBuffer;

            // Make InventoryID read-only
            if (dgvInventory.Columns.Contains("InventoryID"))
                dgvInventory.Columns["InventoryID"].ReadOnly = true;

            // Ensure Discontinued is a checkbox column
            if (dgvInventory.Columns.Contains("Discontinued") &&
                !(dgvInventory.Columns["Discontinued"] is DataGridViewCheckBoxColumn))
            {
                int idx = dgvInventory.Columns["Discontinued"].Index;
                dgvInventory.Columns.RemoveAt(idx);
                var chkCol = new DataGridViewCheckBoxColumn
                {
                    Name = "Discontinued",
                    DataPropertyName = "Discontinued",
                    HeaderText = "Discontinued"
                };
                dgvInventory.Columns.Insert(idx, chkCol);
            }

            // Make all editable columns writable
            string[] editableCols = { "ItemName", "ItemDescription", "CategoryID", "RetailPrice", "Cost", "Quantity", "RestockThreshold", "Discontinued" };
            foreach (string col in editableCols)
            {
                if (dgvInventory.Columns.Contains(col))
                    dgvInventory.Columns[col].ReadOnly = false;
            }

            HighlightRestockRows();
        }



        // On selection change, store the current customer
        private void dgvCustomers_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvCustomers.SelectedRows.Count == 0) return;

            var row = dgvCustomers.SelectedRows[0];

            // PersonID for checkout
            if (dgvCustomers.Columns.Contains("PersonID") &&
                row.Cells["PersonID"].Value != null &&
                int.TryParse(row.Cells["PersonID"].Value.ToString(), out int pid))
                Validation.CurrentManagerSelectedCustomerId = pid;
            else
                Validation.CurrentManagerSelectedCustomerId = null;

            // Prefer LogonName, fallback to FullName
            string display = dgvCustomers.Columns.Contains("LogonName")
                ? row.Cells["LogonName"].Value?.ToString()
                : row.Cells["FullName"].Value?.ToString();

            lblCurrentCustomer.Text = string.IsNullOrWhiteSpace(display)
                ? "Current Customer: (none)"
                : $"Current Customer: {display}";
        }



        // Double-click also selects (handy UX)
        private void dgvCustomers_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = dgvCustomers.Rows[e.RowIndex];
            row.Selected = true;

            // Set a visible cell to avoid “invisible cell” errors
            int colIdx = e.ColumnIndex;
            if (colIdx < 0 || !dgvCustomers.Columns[colIdx].Visible)
            {
                colIdx = dgvCustomers.Columns.Cast<DataGridViewColumn>()
                          .FirstOrDefault(c => c.Visible)?.Index ?? -1;
            }
            if (colIdx >= 0) dgvCustomers.CurrentCell = row.Cells[colIdx];

            // Reuse your selection logic
            dgvCustomers_SelectionChanged(sender, EventArgs.Empty);
        }


        private void tbxSearch_TextChanged_1(object sender, EventArgs e)
        {
            var dt = DBConnect.SearchCustomers(tbxSearch.Text.Trim());
            dgvCustomers.DataSource = dt;
            if (dgvCustomers.Columns.Contains("PersonID"))
                dgvCustomers.Columns["PersonID"].Visible = false;
        }




        private Panel FindCartPanelByInventoryId(int inventoryId)
        {
            foreach (Panel p in flpOrderSummary.Controls.OfType<Panel>())
            {
                if (p.Tag is Validation.OrderLine ol && ol.InventoryID == inventoryId)
                    return p;
            }
            return null;
        }

        private void btnSearchData_Click(object sender, EventArgs e)
        {
            string term = tbxSearchData.Text.Trim();
            _editBuffer = DBConnect.GetPeopleForEdit(term);

            dgvDataEdit.AutoGenerateColumns = true;
            dgvDataEdit.DataSource = _editBuffer;

            // Lock columns the manager must NOT change
            SetReadOnlyIfExists(dgvDataEdit, "PersonID", true);
            SetReadOnlyIfExists(dgvDataEdit, "LogonName", true);
            

            // Optional: make name fields editable (if allowed); access level is display-only here
            // Disallow adding/deleting directly in grid (we insert via Save on added rows in the table)
            dgvDataEdit.AllowUserToAddRows = true;    // allow adding Person rows
            dgvDataEdit.AllowUserToDeleteRows = false;

            // Hook validations for email/phone/zip/state
            dgvDataEdit.CellValidating -= dgvDataEdit_CellValidating;
            dgvDataEdit.CellValidating += dgvDataEdit_CellValidating;

            dgvDataEdit.DataError -= dgvDataEdit_DataError;
            dgvDataEdit.DataError += dgvDataEdit_DataError;
        }

        private void dgvDataEdit_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            
            var row = dgvDataEdit.Rows[e.RowIndex];
            if (row == null || row.IsNewRow) return; // ✅ don’t validate the blank new row

            dgvDataEdit.Rows[e.RowIndex].ErrorText = "";
            string col = dgvDataEdit.Columns[e.ColumnIndex].Name;
            string val = (e.FormattedValue ?? "").ToString();

            if (col == "PositionTitle")
            {
                if (!(new[] { "Customer", "Employee", "Manager" }.Contains(val)))
                {
                    e.Cancel = true;
                    row.ErrorText = "Position must be Customer, Employee, or Manager.";
                    return;
                }
            }
            else if (col == "Email")
            {
                if (!Validation.IsValidEmail(val))
                {
                    e.Cancel = true;
                    row.ErrorText = "Invalid email (40 chars max, proper format).";
                }
            }
            else if (col == "PhonePrimary")
            {
                string raw = Validation.SaveVerification(val);
                if (raw.Length != 10)
                {
                    e.Cancel = true;
                    row.ErrorText = "Primary phone must be exactly 10 digits.";
                }
            }
            else if (col == "PhoneSecondary")
            {
                string raw = Validation.SaveVerification(val);
                if (!string.IsNullOrWhiteSpace(val) && raw.Length != 10)
                {
                    e.Cancel = true;
                    row.ErrorText = "Secondary phone must be 10 digits if provided.";
                }
            }
            else if (col == "Zipcode")
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(val.Trim(), @"^\d{5}$"))
                {
                    e.Cancel = true;
                    row.ErrorText = "Zip code must be exactly 5 digits.";
                }
            }
            else if (col == "State")
            {
                if (string.IsNullOrWhiteSpace(val))
                {
                    e.Cancel = true;
                    row.ErrorText = "Please select a valid U.S. state.";
                }
            }
        }


        private void dgvDataEdit_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            MessageBox.Show("Invalid value. Please check the format.", "Data Entry",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static bool? ToNullableBool(object v)
        {
            if (v == null || v == DBNull.Value) return null;
            return Convert.ToBoolean(v);
        }

        private void BindDataEditGrid(string term)
        {
            _editBuffer = DBConnect.GetPeopleForEdit(term);
            dgvDataEdit.AutoGenerateColumns = true;
            dgvDataEdit.DataSource = _editBuffer;

            // ❌ Do NOT allow creating new rows here
            dgvDataEdit.AllowUserToAddRows = false;
            dgvDataEdit.AllowUserToDeleteRows = false;
            dgvDataEdit.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            dgvDataEdit.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvDataEdit.MultiSelect = false;
            dgvDataEdit.StandardTab = true; // Enter behaves more like Tab

            // Keep immutable fields locked
            SetReadOnlyIfExists(dgvDataEdit, "PersonID", true);
            SetReadOnlyIfExists(dgvDataEdit, "LogonName", true);

            // ✅ Make PositionTitle editable via combo
            ReplaceWithCombo(dgvDataEdit, "PositionTitle", new[] { "Customer", "Employee", "Manager" });

            // ✅ Ensure AccountDisabled/AccountDeleted are checkboxes and editable
            EnsureCheckbox(dgvDataEdit, "AccountDisabled", "Disabled");
            EnsureCheckbox(dgvDataEdit, "AccountDeleted", "Deleted");

            // Validation hooks
            dgvDataEdit.CellValidating -= dgvDataEdit_CellValidating;
            dgvDataEdit.CellValidating += dgvDataEdit_CellValidating;

            dgvDataEdit.DataError -= dgvDataEdit_DataError;
            dgvDataEdit.DataError += dgvDataEdit_DataError;

            // Optional: commit checkbox edits immediately
            dgvDataEdit.CurrentCellDirtyStateChanged -= dgvDataEdit_CurrentCellDirtyStateChanged;
            dgvDataEdit.CurrentCellDirtyStateChanged += dgvDataEdit_CurrentCellDirtyStateChanged;

            dgvDataEdit.KeyDown -= dgvDataEdit_KeyDown;
            dgvDataEdit.KeyDown += dgvDataEdit_KeyDown;
        }

        private void dgvDataEdit_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dgvDataEdit.IsCurrentCellDirty)
                dgvDataEdit.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void ReplaceWithCombo(DataGridView grid, string colName, IEnumerable<string> items)
        {
            if (!grid.Columns.Contains(colName)) return;
            int idx = grid.Columns[colName].Index;
            grid.Columns.RemoveAt(idx);
            var cmb = new DataGridViewComboBoxColumn
            {
                Name = colName,
                DataPropertyName = colName,
                HeaderText = "Position",
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            };
            cmb.DataSource = items.ToList();
            grid.Columns.Insert(idx, cmb);
        }

        private void EnsureCheckbox(DataGridView grid, string colName, string header)
        {
            if (!grid.Columns.Contains(colName)) return;
            if (grid.Columns[colName] is DataGridViewCheckBoxColumn) return;

            int idx = grid.Columns[colName].Index;
            grid.Columns.RemoveAt(idx);
            var chk = new DataGridViewCheckBoxColumn
            {
                Name = colName,
                DataPropertyName = colName,
                HeaderText = header,
                TrueValue = true,
                FalseValue = false
            };
            grid.Columns.Insert(idx, chk);
        }



        private void SetReadOnlyIfExists(DataGridView grid, string colName, bool ro)
        {
            if (grid.Columns.Contains(colName))
                grid.Columns[colName].ReadOnly = ro;
        }

        private void btnSaveData_Click(object sender, EventArgs e)
        {
            dgvDataEdit.EndEdit(DataGridViewDataErrorContexts.Commit);

            // block save if any row shows a validation error
            foreach (DataGridViewRow r in dgvDataEdit.Rows)
            {
                if (!r.IsNewRow && !string.IsNullOrEmpty(r.ErrorText))
                {
                    MessageBox.Show("Please fix highlighted rows before saving.", "Validation");
                    return;
                }
            }

            if (_editBuffer == null)
            {
                MessageBox.Show("Nothing to save.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var changes = _editBuffer.GetChanges();
            if (changes == null || changes.Rows.Count == 0)
            {
                MessageBox.Show("No changes detected.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // disallow adds in this grid
            if (changes.AsEnumerable().Any(r => r.RowState == DataRowState.Added))
            {
                MessageBox.Show("Adding new people is not allowed in this grid. Use the separate Add Person section.",
                    "Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _editBuffer.RejectChanges();
                BindDataEditGrid(tbxSearchData.Text.Trim());
                return;
            }

            // final validation for modified rows
            foreach (DataRow r in changes.Rows)
            {
                if (r.RowState != DataRowState.Modified) continue;

                string email = r.Table.Columns.Contains("Email") ? r["Email"]?.ToString() : null;
                if (!Validation.IsValidEmail(email))
                {
                    MessageBox.Show("Invalid email (40 chars max, proper format).", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string phone1 = r.Table.Columns.Contains("PhonePrimary") ? (r["PhonePrimary"]?.ToString() ?? "") : "";
                string digitsPhone1 = Validation.SaveVerification(phone1);
                if (digitsPhone1.Length != 10)
                {
                    MessageBox.Show("Primary phone must be exactly 10 digits.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string phone2 = r.Table.Columns.Contains("PhoneSecondary") ? (r["PhoneSecondary"]?.ToString() ?? "") : "";
                string digitsPhone2 = Validation.SaveVerification(phone2);
                if (!string.IsNullOrWhiteSpace(phone2) && digitsPhone2.Length != 10)
                {
                    MessageBox.Show("Secondary phone must be 10 digits if provided.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string zip = r.Table.Columns.Contains("Zipcode") ? (r["Zipcode"]?.ToString() ?? "") : "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(zip.Trim(), @"^\d{5}$"))
                {
                    MessageBox.Show("Zip code must be exactly 5 digits.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string state = r.Table.Columns.Contains("State") ? r["State"]?.ToString() : null;
                if (string.IsNullOrWhiteSpace(state))
                {
                    MessageBox.Show("Please select a valid U.S. state.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // 1) Save Person fields (name, address, email, phones, etc.)
            if (!DBConnect.SavePeopleEdits(changes))
            {
                MessageBox.Show("Could not save person details. Please try again.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Helpers to detect column changes and handle NULLs/empties
            bool ColChanged(DataRow rw, string col) =>
                rw.Table.Columns.Contains(col) &&
                !Equals(rw[col, DataRowVersion.Current], rw[col, DataRowVersion.Original]);

            object AsDbNullIfBlank(object v)
            {
                if (v == null || v == DBNull.Value) return DBNull.Value;
                var s = v.ToString();
                return string.IsNullOrWhiteSpace(s) ? (object)DBNull.Value : v;
            }

            // 2) Save account fields only if changed (PositionTitle / AccountDisabled / AccountDeleted)
            var accountRows = changes.AsEnumerable()
                .Where(r => r.RowState == DataRowState.Modified)
                .Select(r => new
                {
                    PersonID = Convert.ToInt32(r["PersonID"]),

                    // PositionTitle:
                    //   - null  => don't update
                    //   - DBNull.Value => set to NULL
                    //   - string => set to that value
                    PositionTitle = ColChanged(r, "PositionTitle")
                        ? AsDbNullIfBlank(r["PositionTitle"])
                        : null,

                    // AccountDisabled (bit):
                    //   - null  => don't update
                    //   - DBNull.Value => set to NULL (if column allows)
                    //   - bool => set to that value
                    AccountDisabled = ColChanged(r, "AccountDisabled")
                        ? (r["AccountDisabled"] == DBNull.Value ? (object)DBNull.Value : (object)Convert.ToBoolean(r["AccountDisabled"]))
                        : null,

                    // AccountDeleted (bit)
                    AccountDeleted = ColChanged(r, "AccountDeleted")
                        ? (r["AccountDeleted"] == DBNull.Value ? (object)DBNull.Value : (object)Convert.ToBoolean(r["AccountDeleted"]))
                        : null
                })
                .Where(x => x.PositionTitle != null || x.AccountDisabled != null || x.AccountDeleted != null)
                .ToList();

            if (accountRows.Count > 0)
            {
                if (!DBConnect.SaveAccountEdits(accountRows))
                {
                    MessageBox.Show("Account changes could not be saved.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            MessageBox.Show("Changes saved.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Rebind to refresh and restore combo/checkbox columns
            BindDataEditGrid(tbxSearchData.Text.Trim());
        }

        private void dgvDataEdit_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            _originalCellValue = dgvDataEdit.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
        }

        
        private void dgvDataEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                dgvDataEdit.EndEdit(DataGridViewDataErrorContexts.Commit);
                e.Handled = true;
                SendKeys.Send("{TAB}");
            }
            else if (e.KeyCode == Keys.Escape && dgvDataEdit.IsCurrentCellInEditMode)
            {
                dgvDataEdit.CancelEdit(); // revert current cell
                e.Handled = true;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            dgvDataEdit.EndEdit();
            _editBuffer?.RejectChanges();
            BindDataEditGrid(tbxSearchData.Text.Trim());
            MessageBox.Show("Edits canceled and data reset.", "Canceled",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnRegister_Click(object sender, EventArgs e)
        {
            bool isValid = Validation.ValidateInput(tbxEnterPass, tbxConfirmPass, cbxTitle, cbxSuffix,
            cbxQuestions1, cbxQuestions2, cbxQuestions3, cbxPosition, tbxPhone1, tbxPhone2, tbxZipcode, cbxState);

            if (!isValid)
                return;

            string email = tbxEmail.Text;

            if (!Validation.IsValidEmail(email))
            {
                MessageBox.Show("Please enter a valid email address (40 characters max).",
                                "Invalid Email", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DBConnect.InsertNewRecord(tbxEnterPass, tbxConfirmPass, tbxFirstName, tbxMiddleName, tbxLastName, tbxAddress,
            tbxCity, tbxZipcode, tbxAnswer1, tbxAnswer2, tbxAnswer3, cbxTitle, cbxSuffix, cbxQuestions1,
            cbxQuestions2, cbxQuestions3, cbxPosition, cbxState, tbxAddress2, tbxAddress3, tbxEmail,
            tbxPhone1, tbxPhone2, tbxEnterUser);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            tbxFirstName.Clear();
            tbxMiddleName.Clear();
            tbxLastName.Clear();
            tbxAddress.Clear();
            tbxAddress2.Clear();
            tbxAddress3.Clear();
            tbxCity.Clear();
            tbxZipcode.Clear();
            tbxEmail.Clear();
            tbxPhone1.Clear();
            tbxPhone2.Clear();
            tbxEnterUser.Clear();
            tbxEnterPass.Clear();
            tbxConfirmPass.Clear();
            tbxAnswer1.Clear();
            tbxAnswer2.Clear();
            tbxAnswer3.Clear();

            cbxTitle.SelectedIndex = -1;
            cbxSuffix.SelectedIndex = -1;
            cbxQuestions1.SelectedIndex = -1;
            cbxQuestions2.SelectedIndex = -1;
            cbxQuestions3.SelectedIndex = -1;
            cbxPosition.SelectedIndex = -1;
        }

        private void cbxShow1_CheckedChanged(object sender, EventArgs e)
        {
            tbxEnterPass.PasswordChar = cbxShow1.Checked ? '\0' : '*';
        }

        private void cbxShow2_CheckedChanged(object sender, EventArgs e)
        {
            tbxEnterPass.PasswordChar = cbxShow2.Checked ? '\0' : '*';
        }

        private void dgvInventory_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            HighlightRestockRows();
        }
        private void dgvInventory_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 &&
               (dgvInventory.Columns[e.ColumnIndex].Name == "Quantity" ||
                dgvInventory.Columns[e.ColumnIndex].Name == "RestockThreshold"))
            {
                HighlightRestockForRow(dgvInventory.Rows[e.RowIndex]);
            }
        }
        private void dgvInventory_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dgvInventory.IsCurrentCellDirty)
                dgvInventory.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
        private void HighlightRestockRows()
        {
            foreach (DataGridViewRow row in dgvInventory.Rows)
                HighlightRestockForRow(row);
        }
        private void HighlightRestockForRow(DataGridViewRow row)
        {
            if (row == null || row.IsNewRow) return;

            int qty = 0, threshold = 0;
            int.TryParse(row.Cells["Quantity"]?.Value?.ToString(), out qty);
            int.TryParse(row.Cells["RestockThreshold"]?.Value?.ToString(), out threshold);

            if (qty < threshold)
            {
                row.DefaultCellStyle.BackColor = Color.MistyRose;
                row.DefaultCellStyle.ForeColor = Color.DarkRed;
            }
            else
            {
                row.DefaultCellStyle.BackColor = dgvInventory.DefaultCellStyle.BackColor;
                row.DefaultCellStyle.ForeColor = dgvInventory.DefaultCellStyle.ForeColor;
            }
        }

        private void dgvInventory_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
           
            var col = dgvInventory.Columns[e.ColumnIndex].Name;
            var val = (e.FormattedValue ?? "").ToString().Trim();
            dgvInventory.Rows[e.RowIndex].ErrorText = "";

            try
            {
                switch (col)
                {
                    case "ItemName":
                        if (string.IsNullOrWhiteSpace(val)) throw new Exception("ItemName is required.");
                        break;
                    case "CategoryID":
                        {
                            if (!int.TryParse(val, out var catId) || catId <= 0)
                                throw new Exception("CategoryID must be a positive integer.");

                            
                            if (!DBConnect.CategoryExists(catId))
                                throw new Exception($"CategoryID {catId} does not exist in Categories.");

                            break;
                        }
                    case "RetailPrice":
                    case "Cost":
                        if (!decimal.TryParse(val, out var d) || d < 0)
                            throw new Exception($"{col} must be a non-negative decimal.");

                        // ✅ Ensure only up to 3 digits before the decimal point
                        if (d >= 1000)
                            throw new Exception($"{col} cannot be more than 3 digits.");

                        break;
                    case "Quantity":
                    case "RestockThreshold":
                        if (!int.TryParse(val, out var q) || q < 0) throw new Exception($"{col} must be a non-negative integer.");
                        break;
                    case "Discontinued":

                        break;
                }
            }
            catch (Exception ex)
            {
                e.Cancel = true;
                dgvInventory.Rows[e.RowIndex].ErrorText = ex.Message;
            }
        }

        private void btnSaveInv_Click(object sender, EventArgs e)
        {
            dgvInventory.EndEdit();

            if (_invBuffer == null)
            {
                MessageBox.Show("Nothing to save.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var changes = _invBuffer.GetChanges(DataRowState.Added | DataRowState.Modified);
            if (changes == null || changes.Rows.Count == 0)
            {
                MessageBox.Show("No changes detected.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Final pass checks
            foreach (DataRow r in changes.Rows)
            {
                string name = r["ItemName"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("ItemName is required on all rows.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!decimal.TryParse(r["RetailPrice"]?.ToString(), out decimal retail) || retail < 0m)
                {
                    MessageBox.Show($"Invalid RetailPrice for {name}.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!decimal.TryParse(r["Cost"]?.ToString(), out decimal cost) || cost < 0m)
                {
                    MessageBox.Show($"Invalid Cost for {name}.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!int.TryParse(r["Quantity"]?.ToString(), out int qty) || qty < 0)
                {
                    MessageBox.Show($"Invalid Quantity for {name}.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!int.TryParse(r["RestockThreshold"]?.ToString(), out int rstk) || rstk < 0)
                {
                    MessageBox.Show($"Invalid RestockThreshold for {name}.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!int.TryParse(r["CategoryID"]?.ToString(), out int catId) || catId <= 0)
                {
                    MessageBox.Show($"Invalid CategoryID for {name}.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Optional: warn if retail < cost
                if (retail < cost)
                {
                    var dlg = MessageBox.Show(
                        $"RetailPrice ({retail:C}) is less than Cost ({cost:C}) for '{name}'. Save anyway?",
                        "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dlg == DialogResult.No) return;
                }
            }

            try
            {
                bool ok = DBConnect.SaveInventoryEdits(changes);
                if (ok)
                {
                    MessageBox.Show("Inventory changes saved.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _invBuffer = DBConnect.GetInventoryForEdit("");
                    dgvInventory.DataSource = _invBuffer;
                    HighlightRestockRows();
                }
                else
                {
                    ShowFriendlyError("We couldn’t save inventory changes. Please review highlighted fields and try again.");
                }
            }
            catch (Exception ex)
            {
                LogException(ex, "SaveInventoryEdits");
                ShowFriendlyError("We couldn’t save inventory changes due to a system error. Please try again or contact support.");
            }
        }

        //display for the promo grid and line 2234 is where the program crashes at. 
        private void BindDiscountGrid()
        {
            SafeEndEdits(dgvDisc, _discBuffer);
            _discBuffer = DBConnect.GetDiscountsForEdit();

            dgvDisc.AutoGenerateColumns = true;     // create columns from schema
            dgvDisc.DataSource = _discBuffer;

            dgvDisc.MultiSelect = false;
            dgvDisc.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvDisc.AllowUserToAddRows = true;
            dgvDisc.AllowUserToDeleteRows = false;

            // Key column read-only
            if (dgvDisc.Columns.Contains("DiscountID"))
                dgvDisc.Columns["DiscountID"].ReadOnly = true;

            // Make date columns nullable and display/commit blanks as NULL
            void MakeDateColNullable(string name)
            {
                if (_discBuffer.Columns.Contains(name))
                    _discBuffer.Columns[name].AllowDBNull = true;

                if (dgvDisc.Columns.Contains(name))
                {
                    var col = dgvDisc.Columns[name];
                    col.DefaultCellStyle.Format = "yyyy-MM-dd";   // pretty print when non-null
                    col.DefaultCellStyle.NullValue = "";          // show blanks for nulls
                    col.DefaultCellStyle.DataSourceNullValue = DBNull.Value; // write NULL back to DataTable
                }
            }
            MakeDateColNullable("StartDate");
            MakeDateColNullable("ExpirationDate");

            // Ensure editable columns are writable
            string[] editableCols =
            {
        "DiscountCode","Description","DiscountLevel","InventoryID",
        "DiscountType","DiscountPercentage","DiscountDollarAmount",
        "StartDate","ExpirationDate"
    };
            foreach (var col in editableCols)
                if (dgvDisc.Columns.Contains(col))
                    dgvDisc.Columns[col].ReadOnly = false;

            // Friendly grid-typing errors (parsing/formatting)
            dgvDisc.DataError -= dgv_DataError_Soft;
            dgvDisc.DataError += dgv_DataError_Soft;

            // Your row-level business validation (InventoryID exists, dates optional, etc.)
            dgvDisc.RowValidating -= dgvDiscounts_RowValidating;
            dgvDisc.RowValidating += dgvDiscounts_RowValidating;
        }


        private static string SafeString(DataRow r, string col) =>
        r.Table.Columns.Contains(col) ? (r[col] == DBNull.Value ? null : r[col]?.ToString()) : null;

        private static int? SafeInt(DataRow r, string col)
        {
            if (!r.Table.Columns.Contains(col) || r[col] == DBNull.Value) return null;
            int x; return int.TryParse(r[col].ToString(), out x) ? x : (int?)null;
        }

        private static decimal? SafeDecimal(DataRow r, string col)
        {
            if (!r.Table.Columns.Contains(col) || r[col] == DBNull.Value) return null;
            decimal d; return decimal.TryParse(r[col].ToString(), out d) ? d : (decimal?)null;
        }

        private static DateTime? SafeDate(DataRow r, string col)
        {
            if (!r.Table.Columns.Contains(col) || r[col] == DBNull.Value) return null;
            DateTime dt; return DateTime.TryParse(r[col].ToString(), out dt) ? dt : (DateTime?)null;
        }

        // where to the save for the discount is called.
        private void btnSaveDisc_Click(object sender, EventArgs e)
        {
            // 1) Force commit + validation of the current edit
            try
            {
                if (dgvDisc.IsCurrentCellInEditMode)
                    dgvDisc.EndEdit();

                dgvDisc.CommitEdit(DataGridViewDataErrorContexts.Commit);

                // Move focus to trigger RowValidating on the current row
                var oldCell = dgvDisc.CurrentCell;
                dgvDisc.CurrentCell = null;

                // If any row reports an error, stop here
                foreach (DataGridViewRow r in dgvDisc.Rows)
                {
                    if (!r.IsNewRow && !string.IsNullOrEmpty(r.ErrorText))
                    {
                        MessageBox.Show($"Please fix: {r.ErrorText}", "Validation",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);

                        // try to restore focus to a safe cell
                        if (oldCell != null && oldCell.RowIndex < dgvDisc.Rows.Count)
                            dgvDisc.CurrentCell = oldCell;
                        return;
                    }
                }
            }
            catch
            {
                // ignore; we still check for row errors below
            }

            // 2) No backing buffer?
            if (_discBuffer == null)
            {
                MessageBox.Show("Nothing to save.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 3) Gather only Added/Modified changes
            var changes = _discBuffer.GetChanges(DataRowState.Added | DataRowState.Modified);
            if (changes == null || changes.Rows.Count == 0)
            {
                MessageBox.Show("No changes detected.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 4) Quick sanity validation (RowValidating already did deep checks)
            foreach (DataRow r in changes.Rows)
            {
                if (r.RowState == DataRowState.Deleted) continue;

                string code = SafeString(r, "DiscountCode");
                if (string.IsNullOrWhiteSpace(code))
                {
                    MessageBox.Show("DiscountCode is required.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int? level = SafeInt(r, "DiscountLevel");
                if (level == null || (level != 0 && level != 1))
                {
                    MessageBox.Show("DiscountLevel must be 0 (cart) or 1 (item).", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int? type = SafeInt(r, "DiscountType");
                if (type == null || (type != 0 && type != 1))
                {
                    MessageBox.Show("DiscountType must be 0 (percent) or 1 (amount).", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Percent is stored as decimal fraction (0.00–1.00)
                decimal? pct = SafeDecimal(r, "DiscountPercentage");
                decimal? amt = SafeDecimal(r, "DiscountDollarAmount");

                if (type == 0)
                {
                    if (pct == null || pct < 0m || pct > 1m)
                    {
                        MessageBox.Show("For percent discounts, DiscountPercentage must be a decimal between 0.00 and 1.00 (e.g., 0.20 for 20%).",
                            "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    // normalize opposite field
                    if (changes.Columns.Contains("DiscountDollarAmount"))
                        r["DiscountDollarAmount"] = DBNull.Value;
                }
                else // type == 1
                {
                    if (amt == null || amt <= 0m)
                    {
                        MessageBox.Show("For amount discounts, DiscountDollarAmount must be a positive number.",
                            "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    // normalize opposite field
                    if (changes.Columns.Contains("DiscountPercentage"))
                        r["DiscountPercentage"] = DBNull.Value;
                }

                // Dates are optional; if both present, enforce start <= end.
                DateTime? start = SafeDate(r, "StartDate");
                DateTime? end = SafeDate(r, "ExpirationDate");
                if (start.HasValue && end.HasValue && start.Value.Date > end.Value.Date)
                {
                    MessageBox.Show("StartDate must be on or before ExpirationDate.",
                        "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Item-level must have an InventoryID; existence already checked in RowValidating.
                if (level == 1)
                {
                    int? invId = SafeInt(r, "InventoryID");
                    if (invId == null)
                    {
                        MessageBox.Show("Item-level discounts must specify a valid InventoryID.",
                            "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    // cart-level: clear InventoryID
                    if (changes.Columns.Contains("InventoryID"))
                        r["InventoryID"] = DBNull.Value;
                }
            }

            // 5) Save & handle FK errors nicely
            try
            {
                bool ok = DBConnect.SaveDiscountEdits(changes);
                if (ok)
                {
                    MessageBox.Show("Discount changes saved.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    BindDiscountGrid(); // refresh and clear row states
                }
                else
                {
                    MessageBox.Show("We couldn’t save discount changes. Please review values and try again.",
                        "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (SQLiteException ex)
            {
                var msg = ex.Message ?? "";
                if (msg.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("FK_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    MessageBox.Show(
                        "Invalid InventoryID on an item-level discount. Enter an InventoryID that exists, or leave InventoryID blank for a cart-level discount.",
                        "Invalid InventoryID",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("Error saving discounts:\n" + ex.Message,
                        "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BindOrdersGrid()
        {
            _ordersBuffer = DBConnect.GetAllOrders();
            dgvOrders.AutoGenerateColumns = true;
            dgvOrders.DataSource = _ordersBuffer;

            dgvOrders.ReadOnly = true;
            dgvOrders.MultiSelect = false;
            dgvOrders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvOrders.AllowUserToAddRows = false;
            dgvOrders.AllowUserToDeleteRows = false;

            // Hide raw IDs if you prefer
            if (dgvOrders.Columns.Contains("CustomerPersonID"))
                dgvOrders.Columns["CustomerPersonID"].Visible = false;
            if (dgvOrders.Columns.Contains("EmployeePersonID"))
                dgvOrders.Columns["EmployeePersonID"].Visible = false;

            // Nice headers
            if (dgvOrders.Columns.Contains("OrderID"))
                dgvOrders.Columns["OrderID"].HeaderText = "Order #";
            if (dgvOrders.Columns.Contains("OrderDate"))
                dgvOrders.Columns["OrderDate"].HeaderText = "Date";
            if (dgvOrders.Columns.Contains("CustomerLogon"))
                dgvOrders.Columns["CustomerLogon"].HeaderText = "Customer";
            if (dgvOrders.Columns.Contains("EmployeeLogon"))
                dgvOrders.Columns["EmployeeLogon"].HeaderText = "Employee";

            // Hook selection
            dgvOrders.SelectionChanged -= dgvOrders_SelectionChanged;
            dgvOrders.SelectionChanged += dgvOrders_SelectionChanged;

            // Load first order’s details if exists
            if (dgvOrders.Rows.Count > 0)
                LoadSelectedOrderDetails();
        }

        private void dgvOrders_SelectionChanged(object sender, EventArgs e)
        {
            LoadSelectedOrderDetails();
        }

        private void LoadSelectedOrderDetails()
        {
            if (dgvOrders.SelectedRows.Count == 0)
            {
                dgvOrderReports.DataSource = null;
                return;
            }

            var row = dgvOrders.SelectedRows[0];
            if (row.Cells["OrderID"]?.Value == null) return;

            int orderId;
            if (!int.TryParse(row.Cells["OrderID"].Value.ToString(), out orderId)) return;

            _orderDetailsBuffer = DBConnect.GetOrderDetails(orderId);
            dgvOrderReports.AutoGenerateColumns = true;
            dgvOrderReports.DataSource = _orderDetailsBuffer;

            dgvOrderReports.ReadOnly = true;
            dgvOrderReports.MultiSelect = false;
            dgvOrderReports.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvOrderReports.AllowUserToAddRows = false;
            dgvOrderReports.AllowUserToDeleteRows = false;


            if (dgvOrderReports.Columns.Contains("ItemName"))
                dgvOrderReports.Columns["ItemName"].HeaderText = "Item";
            if (dgvOrderReports.Columns.Contains("Cost"))
                dgvOrderReports.Columns["Cost"].HeaderText = "Unit Cost";
            if (dgvOrderReports.Columns.Contains("Quantity"))
                dgvOrderReports.Columns["Quantity"].HeaderText = "Qty";
            if (dgvOrderReports.Columns.Contains("LineTotal"))
                dgvOrderReports.Columns["LineTotal"].HeaderText = "Line Total";
        }

        private void btnExport_Click(object sender, EventArgs e)
        {

            if (dgvOrders.SelectedRows.Count == 0 || _orderDetailsBuffer == null || _orderDetailsBuffer.Rows.Count == 0)
            {
                MessageBox.Show("Please select an order with details to export.", "Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var orderRow = dgvOrders.SelectedRows[0];

            string orderId = orderRow.Cells["OrderID"]?.Value?.ToString() ?? "";
            string orderDate = orderRow.Cells["OrderDate"]?.Value?.ToString() ?? "";
            string customer = orderRow.Cells["CustomerLogon"]?.Value?.ToString() ?? "";
            string employee = orderRow.Cells["EmployeeLogon"]?.Value?.ToString() ?? "";
            string discount = orderRow.Cells["DiscountID"]?.Value?.ToString() ?? "";

            // --- Resolve EmployeeID (PersonID) for the employee who handled the order ---
            string employeeIdStr = "";
            if (dgvOrders.Columns.Contains("EmployeeID"))
                employeeIdStr = orderRow.Cells["EmployeeID"]?.Value?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(employeeIdStr))
            {
                string loggedEmpLogon =
                    !string.IsNullOrWhiteSpace(Validation.LoggedInManagerUsername) ? Validation.LoggedInManagerUsername :
                    (!string.IsNullOrWhiteSpace(Validation.LoggedInEmployeeUsername) ? Validation.LoggedInEmployeeUsername : null);

                if (!string.IsNullOrWhiteSpace(loggedEmpLogon))
                    employeeIdStr = DBConnect.GetPersonIDByUsername(loggedEmpLogon);
            }

            if (string.IsNullOrWhiteSpace(employeeIdStr) && !string.IsNullOrWhiteSpace(employee))
                employeeIdStr = DBConnect.GetPersonIDByUsername(employee);
            // --- end resolve EmployeeID ---

            // ---- Build line items table & compute subtotal ----
            decimal subtotal = 0m;
            foreach (DataRow r in _orderDetailsBuffer.Rows)
            {
                if (decimal.TryParse(r["LineTotal"]?.ToString(), out decimal dLine))
                    subtotal += dLine;
            }

            // ---- Compute discount amount (cart-level or item-level) ----
            decimal discountAmount = 0m;
            int discountId;
            if (int.TryParse(discount, out discountId))
            {
                try
                {
                    using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                    using (var cmd = new SQLiteCommand(@"
                SELECT DiscountLevel, InventoryID, DiscountType, DiscountPercentage, DiscountDollarAmount
                FROM Discounts
                WHERE DiscountID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", discountId);
                        conn.Open();
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (rd.Read())
                            {
                                int level = rd["DiscountLevel"] != DBNull.Value ? Convert.ToInt32(rd["DiscountLevel"]) : 0;  // 0 cart, 1 item
                                int? invId = rd["InventoryID"] != DBNull.Value ? Convert.ToInt32(rd["InventoryID"]) : (int?)null;
                                int type = rd["DiscountType"] != DBNull.Value ? Convert.ToInt32(rd["DiscountType"]) : 0;      // 0 % , 1 $
                                decimal? pct = rd["DiscountPercentage"] != DBNull.Value ? Convert.ToDecimal(rd["DiscountPercentage"]) : (decimal?)null;
                                decimal? amt = rd["DiscountDollarAmount"] != DBNull.Value ? Convert.ToDecimal(rd["DiscountDollarAmount"]) : (decimal?)null;

                                if (level == 0)
                                {
                                    // Cart-level
                                    if (type == 0 && pct.HasValue)
                                        discountAmount = Math.Round(subtotal * pct.Value, 2);
                                    else if (type == 1 && amt.HasValue)
                                        discountAmount = amt.Value;
                                }
                                else if (level == 1 && invId.HasValue)
                                {
                                    // Item-level: sum only matching rows
                                    foreach (DataRow r in _orderDetailsBuffer.Rows)
                                    {
                                        if (!int.TryParse(r["InventoryID"]?.ToString(), out int rowInv)) continue;

                                        if (rowInv == invId.Value)
                                        {
                                            decimal unit = 0m; int q = 0;
                                            decimal.TryParse(r["Cost"]?.ToString(), out unit);
                                            int.TryParse(r["Quantity"]?.ToString(), out q);

                                            if (type == 0 && pct.HasValue)
                                                discountAmount += Math.Round((unit * q) * pct.Value, 2);
                                            else if (type == 1 && amt.HasValue)
                                                discountAmount += (amt.Value * q);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error calculating discount: " + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            // Clamp discount to subtotal
            if (discountAmount < 0) discountAmount = 0m;
            if (discountAmount > subtotal) discountAmount = subtotal;

            decimal discountedSubtotal = subtotal - discountAmount;
            decimal tax = Math.Round(discountedSubtotal * 0.0825m, 2);
            decimal total = discountedSubtotal + tax;

            // ---- Build HTML ----
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<html><head><meta charset='utf-8'><title>Order Report</title>");
            sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif} table{border-collapse:collapse} th,td{border:1px solid #ccc;padding:6px 10px;text-align:left}</style>");
            sb.AppendLine("</head><body>");

            sb.AppendLine("<h2>Order Report</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Order #</th><td>{orderId}</td></tr>");
            sb.AppendLine($"<tr><th>Date</th><td>{orderDate}</td></tr>");
            sb.AppendLine($"<tr><th>Customer</th><td>{customer}</td></tr>");
            sb.AppendLine($"<tr><th>Employee</th><td>{employee}</td></tr>");
            sb.AppendLine($"<tr><th>EmployeeID</th><td>{(string.IsNullOrWhiteSpace(employeeIdStr) ? "-" : employeeIdStr)}</td></tr>");
            sb.AppendLine($"<tr><th>DiscountID</th><td>{(string.IsNullOrWhiteSpace(discount) ? "-" : discount)}</td></tr>");
            sb.AppendLine("</table><br/>");

            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>InventoryID</th><th>Item</th><th>Unit Cost</th><th>Qty</th><th>Line Total</th></tr>");
            foreach (DataRow r in _orderDetailsBuffer.Rows)
            {
                string invId = r["InventoryID"]?.ToString() ?? "";
                string item = r["ItemName"]?.ToString() ?? "";
                string cost = "";
                string qty = "";
                string line = "";

                if (decimal.TryParse(r["Cost"]?.ToString(), out decimal dCost)) cost = dCost.ToString("F2");
                if (int.TryParse(r["Quantity"]?.ToString(), out int iQty)) qty = iQty.ToString();
                if (decimal.TryParse(r["LineTotal"]?.ToString(), out decimal dLine)) line = dLine.ToString("F2");

                sb.AppendLine($"<tr><td>{invId}</td><td>{item}</td><td>${cost}</td><td>{qty}</td><td>${line}</td></tr>");
            }
            sb.AppendLine("</table>");

            sb.AppendLine($"<p><strong>Subtotal:</strong> ${subtotal:F2}</p>");
            sb.AppendLine($"<p><strong>Amount Discounted:</strong> -${discountAmount:F2}</p>");
            sb.AppendLine($"<p><strong>Tax (8.25%):</strong> ${tax:F2}</p>");
            sb.AppendLine($"<h3>Final Total: ${total:F2}</h3>");

            sb.AppendLine("</body></html>");

            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string folder = System.IO.Path.Combine(documents, "OneStopOrderReports");
                System.IO.Directory.CreateDirectory(folder);

                string file = System.IO.Path.Combine(folder, $"Order_{orderId}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                System.IO.File.WriteAllText(file, sb.ToString());

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error exporting report:\n" + ex.Message, "Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void tbxCustomerReportLogon_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                btnCustomerReport.PerformClick();
            }
        }

        private void btnCustomerReport_Click(object sender, EventArgs e)
        {
            DateTime start = dtpStart.Value.Date;
            DateTime end = dtpEnd.Value.Date;

            string logon = string.IsNullOrWhiteSpace(tbxCustomerReportLogon.Text)
                ? null
                : tbxCustomerReportLogon.Text.Trim();

            var dt = DBConnect.GetCustomerOrdersReport(start, end, logon);
            dgvReports.DataSource = dt;
            AutoSizeAndFormatReportGrid();
        }

        private void btnProfitsReport_Click(object sender, EventArgs e)
        {
            DateTime startDate = dtpStart.Value.Date;
            DateTime endDate = dtpEnd.Value.Date.AddDays(1).AddTicks(-1); // inclusive
            var dt = DBConnect.GetProfitsReport(startDate, endDate);
            dgvReports.ReadOnly = true;
            dgvReports.DataSource = dt;
        }



        private void btnPrintReport_Click(object sender, EventArgs e)
        {
            var dt = dgvReports != null ? dgvReports.DataSource as DataTable : null;
            if (dt == null || dt.Rows.Count == 0)
            {
                MessageBox.Show("No report data to export.", "Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Heuristic title based on columns
            string title = "Report";
            if (dt.Columns.Contains("SaleDate") && dt.Columns.Contains("Profit")) title = "Profits Report";
            else if (dt.Columns.Contains("OrderID") && dt.Columns.Contains("OrderTotal")) title = "Customer Orders Report";
            else if (dt.Columns.Contains("QuantityOnHand") && dt.Columns.Contains("RestockThreshold")) title = "Inventory Report";

            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string folder = System.IO.Path.Combine(documents, "OneStopReports");
                System.IO.Directory.CreateDirectory(folder);

                string file = System.IO.Path.Combine(folder,
                    $"{title.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.html");

                System.IO.File.WriteAllText(file, BuildHtmlFromDataTable(dt, title));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error exporting report:\n" + ex.Message, "Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AutoSizeAndFormatReportGrid()
        {
            if (dgvReports == null || dgvReports.Columns.Count == 0) return;
            dgvReports.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;

            // Format suggested numeric columns if present
            if (dgvReports.Columns.Contains("GrossSales"))
                dgvReports.Columns["GrossSales"].DefaultCellStyle.Format = "C2";
            if (dgvReports.Columns.Contains("TotalCost"))
                dgvReports.Columns["TotalCost"].DefaultCellStyle.Format = "C2";
            if (dgvReports.Columns.Contains("Profit"))
                dgvReports.Columns["Profit"].DefaultCellStyle.Format = "C2";
            if (dgvReports.Columns.Contains("OrderTotal"))
                dgvReports.Columns["OrderTotal"].DefaultCellStyle.Format = "C2";
            if (dgvReports.Columns.Contains("Price"))
                dgvReports.Columns["Price"].DefaultCellStyle.Format = "C2";
            if (dgvReports.Columns.Contains("Cost"))
                dgvReports.Columns["Cost"].DefaultCellStyle.Format = "C2";
        }

        private void HighlightRestockRowsInReport()
        {
            if (dgvReports?.Rows == null) return;
            foreach (DataGridViewRow row in dgvReports.Rows)
            {
                if (row.IsNewRow) continue;

                if (dgvReports.Columns.Contains("QuantityOnHand") &&
                    dgvReports.Columns.Contains("RestockThreshold"))
                {
                    var qObj = row.Cells["QuantityOnHand"].Value;
                    var rObj = row.Cells["RestockThreshold"].Value;

                    if (qObj != null && rObj != null &&
                        int.TryParse(qObj.ToString(), out int q) &&
                        int.TryParse(rObj.ToString(), out int rt))
                    {
                        if (q < rt)
                        {
                            row.DefaultCellStyle.BackColor = Color.MistyRose; // highlight needs restock
                        }
                    }
                }
            }
        }

        private string BuildHtmlFromDataTable(DataTable dt, string title)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<html><head><meta charset='utf-8'><title>" + WebUtility.HtmlEncode(title) + "</title>");
            sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif} table{border-collapse:collapse} th,td{border:1px solid #ccc;padding:6px 10px;text-align:left}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h2>" + WebUtility.HtmlEncode(title) + "</h2>");
            sb.AppendLine("<table><tr>");

            // headers
            foreach (DataColumn col in dt.Columns)
                sb.AppendLine("<th>" + WebUtility.HtmlEncode(col.ColumnName) + "</th>");
            sb.AppendLine("</tr>");

            // rows
            foreach (DataRow row in dt.Rows)
            {
                sb.AppendLine("<tr>");
                foreach (DataColumn col in dt.Columns)
                {
                    string val = row[col] == DBNull.Value ? "" : row[col].ToString();
                    sb.AppendLine("<td>" + WebUtility.HtmlEncode(val) + "</td>");
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private void btnSelect_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select Inventory Image";
                ofd.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*";
                ofd.CheckFileExists = true;
                ofd.Multiselect = false;

                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    _selectedImagePath = ofd.FileName;

                    try
                    {
                        using (var fs = new FileStream(_selectedImagePath, FileMode.Open, FileAccess.Read))
                        {
                            // load a copy so the file doesn't remain locked
                            pbxInventory.Image = Image.FromStream(fs);
                        }
                        pbxInventory.SizeMode = PictureBoxSizeMode.Zoom;
                    }
                    catch (Exception ex)
                    {
                        _selectedImagePath = null;
                        pbxInventory.Image = null;
                        MessageBox.Show("Could not load image: " + ex.Message, "Image Preview",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void btnUpload_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tbxCustomerPic.Text) || !int.TryParse(tbxCustomerPic.Text.Trim(), out int inventoryId) || inventoryId <= 0)
            {
                MessageBox.Show("Please enter a valid Inventory ID in the label before uploading.",
                    "Upload", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedImagePath) || !File.Exists(_selectedImagePath))
            {
                MessageBox.Show("Please select an image first.", "Upload",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                byte[] imageBytes = File.ReadAllBytes(_selectedImagePath);

                bool ok = DBConnect.UpdateInventoryImage(inventoryId, imageBytes);
                if (ok)
                {
                    MessageBox.Show("Image uploaded successfully.", "Upload",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("No rows were updated. Verify the Inventory ID exists.",
                        "Upload", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error uploading image: " + ex.Message, "Upload",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnInvAll_Click(object sender, EventArgs e)
        {
            dgvReports.ReadOnly = true;
            dgvReports.DataSource = DBConnect.GetInventoryReport_All();
        }

        private void btnInvSale_Click(object sender, EventArgs e)
        {
            dgvReports.ReadOnly = true;
            dgvReports.DataSource = DBConnect.GetInventoryReport_ForSale();
        }

        private void btnInvRestock_Click(object sender, EventArgs e)
        {
            dgvReports.ReadOnly = true;
            dgvReports.DataSource = DBConnect.GetInventoryReport_NeedsRestock();

        }
        // this is what we are working on. line 2850 when chnaged from unchanged wont save null.
        private void dgvDiscounts_RowValidating(object sender, DataGridViewCellCancelEventArgs e)
        {
            var row = dgvDisc.Rows[e.RowIndex];
            if (row == null || row.IsNewRow) return;

            // If unchanged, don't block
            if (row.DataBoundItem is DataRowView drv && drv.Row.RowState == DataRowState.Unchanged)
            {
                row.ErrorText = "";
                return;
            }

            string GetStr(string col) => Convert.ToString(row.Cells[col]?.Value ?? "").Trim();
            bool TryInt(string col, out int v) => int.TryParse(GetStr(col), out v);
            bool TryDec(string col, out decimal v) => decimal.TryParse(GetStr(col), out v);

            // Parse a nullable date. Blank/NULL/MinValue => null. If text is present but unparsable => Fail.
            DateTime? ReadNullableDateOrFail(string col)
            {
                var v = row.Cells[col]?.Value;
                if (v == null || v == DBNull.Value) return null;
                if (v is DateTime dt)
                {
                    if (dt == DateTime.MinValue) return null;
                    return dt.Date;
                }
                var s = GetStr(col);
                if (string.IsNullOrEmpty(s)) return null;
                if (DateTime.TryParse(s, out var parsed)) return parsed.Date;

                Fail($"{col} must be yyyy-MM-dd.");
                return null; // signal failure through e.Cancel
            }

            void Fail(string msg)
            {
                row.ErrorText = msg;
                e.Cancel = true;
            }

            row.ErrorText = "";

            // --- Required text fields ---
            var code = GetStr("DiscountCode");
            var desc = GetStr("Description");
            if (string.IsNullOrWhiteSpace(code)) { Fail("DiscountCode is required."); return; }
            if (string.IsNullOrWhiteSpace(desc)) { Fail("Description is required."); return; }

            // --- Level & Type ---
            if (!TryInt("DiscountLevel", out var level) || (level != 0 && level != 1))
            { Fail("DiscountLevel must be 0 (cart) or 1 (item)."); return; }

            if (!TryInt("DiscountType", out var type) || (type != 0 && type != 1))
            { Fail("DiscountType must be 0 (percent) or 1 (amount)."); return; }

            // --- FIRST: InventoryID existence (so its error isn't masked by date errors) ---
            if (level == 1)
            {
                if (!TryInt("InventoryID", out var invId))
                { Fail("InventoryID required for item-level discounts."); return; }

                bool invExists = false;
                try
                {
                    using (var conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                    using (var cmd = new SQLiteCommand(
                        "SELECT 1 FROM Inventory WHERE InventoryID = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", invId);
                        conn.Open();
                        invExists = cmd.ExecuteScalar() != null;
                    }
                }
                catch (Exception ex)
                {
                    LogException(ex, "Discounts_RowValidating Inventory exists check");
                    Fail("Could not validate InventoryID due to a database error.");
                    return;
                }

                if (!invExists)
                {
                    Fail($"InventoryID {invId} does not exist.");
                    return;
                }
            }
            else
            {
                // Cart-level discount should not target an item
                row.Cells["InventoryID"].Value = DBNull.Value;
            }

            // --- Amount / Percent ---
            decimal pct = 0m, amt = 0m;
            bool hasPct = TryDec("DiscountPercentage", out pct);
            bool hasAmt = TryDec("DiscountDollarAmount", out amt);

            if (type == 0) // percent stored as 0.00–1.00 (e.g., 0.20 for 20%)
            {
                if (!hasPct || pct < 0m || pct > 1m)
                { Fail("DiscountPercentage must be a decimal between 0.00 and 1.00 (e.g., 0.20 for 20%)."); return; }
                row.Cells["DiscountDollarAmount"].Value = DBNull.Value; // normalize
            }
            else // amount
            {
                if (!hasAmt || amt <= 0m)
                { Fail("DiscountDollarAmount must be a positive number."); return; }
                row.Cells["DiscountPercentage"].Value = DBNull.Value;   // normalize
            }

            // --- Dates (optional; past dates allowed) ---
            var start = ReadNullableDateOrFail("StartDate");
            if (e.Cancel) return; // failed parsing

            var end = ReadNullableDateOrFail("ExpirationDate");
            if (e.Cancel) return; // failed parsing

            // Guard for SQL Server DATE range
            var sqlMin = new DateTime(1753, 1, 1);
            if (start.HasValue && start.Value < sqlMin) { Fail("StartDate must be on or after 1753-01-01."); return; }
            if (end.HasValue && end.Value < sqlMin) { Fail("ExpirationDate must be on or after 1753-01-01."); return; }

            // Only enforce ordering when both present
            if (start.HasValue && end.HasValue && start.Value > end.Value)
            {
                Fail("StartDate must be on or before ExpirationDate.");
                return;
            }

            // Normalize blanks to NULL for DB
            if (!start.HasValue) row.Cells["StartDate"].Value = DBNull.Value;
            if (!end.HasValue) row.Cells["ExpirationDate"].Value = DBNull.Value;

            // --- Prevent duplicate DiscountCode in the current buffer (case-insensitive simple check) ---
            try
            {
                var view = dgvDisc.DataSource as DataTable ?? _discBuffer;
                if (view != null)
                {
                    string esc = code.Replace("'", "''");
                    var matches = view.Select($"DiscountCode LIKE '{esc}'");
                    // if there is more than one row with the same code, it's a duplicate
                    int count = matches?.Length ?? 0;
                    if (count > 1)
                    {
                        Fail($"Duplicate DiscountCode '{code}' found. Codes must be unique.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex, "Duplicate DiscountCode check");
                // Soft warning only
                row.ErrorText = "Warning: could not check duplicate DiscountCode due to a system error.";
            }
        }



        private void btnClearDsicounts_Click(object sender, EventArgs e)
        {

            dgvDisc.RowValidating -= dgvDiscounts_RowValidating;

            try
            {
                // Abort any in-progress cell edit
                try
                {
                    if (dgvDisc.IsCurrentCellInEditMode)
                        dgvDisc.CancelEdit(); // discard the current cell edit
                }
                catch { /* ignore */ }

                // Abort any pending data-binding edits
                try
                {
                    var cm = (CurrencyManager)BindingContext[dgvDisc.DataSource];
                    cm?.CancelCurrentEdit();
                }
                catch { /* ignore */ }

                var row = dgvDisc.CurrentRow;
                if (row == null) return;

                var drv = row.DataBoundItem as DataRowView;
                if (drv == null)
                {
                    // Unbound (unlikely). If it's a brand-new grid row, remove it.
                    if (!row.IsNewRow)
                        this.BeginInvoke(new Action(() => { if (dgvDisc.Rows.Contains(row)) dgvDisc.Rows.Remove(row); }));
                    return;
                }

                if (drv.IsNew || drv.Row.RowState == DataRowState.Added)
                {
                    // Cancel a brand-new, unsaved row and remove it after the event stack unwinds
                    try { drv.CancelEdit(); } catch { }
                    this.BeginInvoke(new Action(() =>
                    {
                        if (!row.IsNewRow && dgvDisc.Rows.Contains(row))
                            dgvDisc.Rows.Remove(row);
                    }));
                }
                else if (drv.Row.RowState == DataRowState.Modified)
                {
                    // Revert to last saved values
                    try { drv.CancelEdit(); } catch { }
                    try { drv.Row.RejectChanges(); } catch { }
                }
                // else: unchanged & saved – nothing to clear

                row.ErrorText = string.Empty;
                dgvDisc.CurrentCell = null; // leave edit mode
                dgvDisc.Refresh();
            }
            finally
            {
                // Re-enable your validation
                dgvDisc.RowValidating += dgvDiscounts_RowValidating;
            }
        }

        private void btnLoadOrders_Click(object sender, EventArgs e)
        {
            DateTime start = dtpStartOrders.Value.Date;
            DateTime end = dtpEndOrders.Value.Date;
            if (end < start)
            {
                MessageBox.Show("End date must be on or after the start date.", "Invalid Range",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // 2) Query orders in range (totals must already reflect discounts in your DB method)
                var dt = DBConnect.GetCustomerOrdersReport(start, end);

                // 3) Bind to grid
                dgvOrders.AutoGenerateColumns = true;
                dgvOrders.DataSource = dt;

                // Read-only display grid
                dgvOrders.ReadOnly = true;
                dgvOrders.MultiSelect = false;
                dgvOrders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dgvOrders.AllowUserToAddRows = false;
                dgvOrders.AllowUserToDeleteRows = false;

                // Optional cosmetics / hide raw IDs if present
                if (dgvOrders.Columns.Contains("CustomerPersonID"))
                    dgvOrders.Columns["CustomerPersonID"].Visible = false;
                if (dgvOrders.Columns.Contains("EmployeePersonID"))
                    dgvOrders.Columns["EmployeePersonID"].Visible = false;

                if (dgvOrders.Columns.Contains("OrderID"))
                    dgvOrders.Columns["OrderID"].HeaderText = "Order #";
                if (dgvOrders.Columns.Contains("OrderDate"))
                    dgvOrders.Columns["OrderDate"].HeaderText = "Date";
                if (dgvOrders.Columns.Contains("CustomerLogon"))
                    dgvOrders.Columns["CustomerLogon"].HeaderText = "Customer";
                if (dgvOrders.Columns.Contains("EmployeeLogon"))
                    dgvOrders.Columns["EmployeeLogon"].HeaderText = "Employee";
                if (dgvOrders.Columns.Contains("OrderTotal"))
                    dgvOrders.Columns["OrderTotal"].HeaderText = "Total";

                // 4) Hook selection to load details (ensure handler attached once)
                dgvOrders.SelectionChanged -= dgvOrders_SelectionChanged;
                dgvOrders.SelectionChanged += dgvOrders_SelectionChanged;

                // 5) Load details for the first row (or clear if none)
                if (dgvOrders.Rows.Count > 0)
                    LoadSelectedOrderDetails();
                else
                    dgvOrderReports.DataSource = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading orders:\n" + ex.Message, "Orders",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowFriendlyError(string userMessage, string title = "Error")
        {
            MessageBox.Show(userMessage, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void LogException(Exception ex, string context = "")
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OneStop", "logs");
                Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, "app.log");
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {context}\r\n{ex}\r\n\r\n");
            }
            catch { /* swallow logging failures */ }
        }

        // One handler that both grids can share
        private void dgv_DataError_Soft(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            var grid = sender as DataGridView;
            string colName = grid?.Columns[e.ColumnIndex].HeaderText ?? "value";
            ShowFriendlyError($"Invalid {colName}. Please correct the value and try again.");
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            Validation.Logout();

            MessageBox.Show("You have been logged out.",
                            "Logout Successful",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

            // Return to login screen
            frmMain login = new frmMain();
            login.Show();

            this.Close();
        }
    }
}
