using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using static OneStop.DBConnect;

namespace OneStop
{
    public partial class CustomerView : Form
    {
        public string LoggedInUsername { get; set; }

        private string currentCategory = "All";
        private DiscountInfo appliedDiscount;
        private decimal appliedDiscountAmount;

        public CustomerView(string username)
        {
            InitializeComponent();

            Validation.LoggedInCustomerUsername = username;
        }

        public CustomerView()
        {
            InitializeComponent();
            LoggedInUsername = null;

            btnLogout.Visible = Validation.IsCustomerLoggedIn();
        }

        public Label LblName => lblName;
        public Label LblCurrentStock => lblCurrentStock;






        private void CustomerView_Load(object sender, EventArgs e)
        {
            currentCategory = "All";

            cbxFilter.Items.AddRange(new string[]
            {
                "Default",
                "Price: Low to High",
                "Price: High to Low",
                "Stock: Low to High",
                "Stock: High to Low"
            });
            cbxFilter.SelectedIndex = 0;

            string sortOption = cbxFilter.SelectedItem?.ToString() ?? "Default";
            string searchTerm = tbxInputSearch.Text.Trim();

            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, searchTerm, sortOption);

        }



        private void UpdateTotal()
        {
            decimal subtotal = 0;
            decimal discountAmount = 0;
            string code = tbxAddDiscount.Text.Trim();

            // 🔹 Remove any old totals first (to avoid duplicates)
            foreach (var label in flpOrderSummary.Controls.OfType<Label>()
                     .Where(l => l.Tag != null && l.Tag.ToString() == "Totals").ToList())
            {
                flpOrderSummary.Controls.Remove(label);
                label.Dispose();
            }

            // 🔹 Calculate subtotal from all items in cart
            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
            {
                Label priceLabel = panel.Controls.OfType<Label>()
                    .FirstOrDefault(l => l.Text.StartsWith("Price: $"));
                TextBox qtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();

                if (priceLabel != null && qtyBox != null &&
                    decimal.TryParse(priceLabel.Text.Replace("Price: $", ""), out decimal price) &&
                    int.TryParse(qtyBox.Text, out int qty))
                {
                    subtotal += price * qty;
                }
            }

            // 🔹 If there’s a discount code, process it
            if (appliedDiscount != null)
            {
                try
                {
                    using (SQLiteConnection conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                    {
                        conn.Open();
                        using (SQLiteTransaction transaction = conn.BeginTransaction())
                        {
                            // ✅ CART-LEVEL DISCOUNTS
                            if (appliedDiscount.DiscountLevel == 0)
                            {
                                // Cart-Level Percentage (0/0)
                                if (appliedDiscount.DiscountType == 0 && appliedDiscount.DiscountPercentage.HasValue)
                                {
                                    discountAmount = subtotal * (appliedDiscount.DiscountPercentage.Value / 100);
                                }
                                // Cart-Level Dollar Amount (0/1)
                                else if (appliedDiscount.DiscountType == 1 && appliedDiscount.DiscountDollarAmount.HasValue)
                                {
                                    discountAmount = appliedDiscount.DiscountDollarAmount.Value;
                                }
                            }
                            // ✅ ITEM-LEVEL DISCOUNTS
                            else if (appliedDiscount.DiscountLevel == 1)
                            {
                                foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                                {
                                    Label priceLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Price: $"));
                                    Label nameLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Name:"));
                                    TextBox qtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();

                                    if (priceLabel == null || nameLabel == null || qtyBox == null) continue;

                                    if (!decimal.TryParse(priceLabel.Text.Replace("Price: $", ""), out decimal price) ||
                                        !int.TryParse(qtyBox.Text, out int qty)) continue;

                                    string itemName = nameLabel.Text.Replace("Name: ", "");
                                    int? itemInventoryId = DBConnect.GetInventoryIDByName(itemName, conn, transaction);

                                    // ✅ Only apply discount if item matches InventoryID in discount
                                    if (appliedDiscount.InventoryID.HasValue && appliedDiscount.InventoryID.Value == itemInventoryId)
                                    {
                                        // Item-Level Percentage (1/0)
                                        if (appliedDiscount.DiscountType == 0 && appliedDiscount.DiscountPercentage.HasValue)
                                        {
                                            discountAmount += (price * qty) * (appliedDiscount.DiscountPercentage.Value / 100);
                                        }
                                        // Item-Level Dollar Amount (1/1)
                                        else if (appliedDiscount.DiscountType == 1 && appliedDiscount.DiscountDollarAmount.HasValue)
                                        {
                                            discountAmount += appliedDiscount.DiscountDollarAmount.Value * qty;
                                        }
                                    }
                                }
                            }

                            transaction.Commit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error calculating discount: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // 🔹 Calculate totals
            decimal discountedSubtotal = subtotal - discountAmount;
            if (discountedSubtotal < 0) discountedSubtotal = 0; // prevent negative totals
            decimal taxAmount = discountedSubtotal * 0.0825m;
            decimal finalTotal = discountedSubtotal + taxAmount;

            // 🔹 Only add totals if there are items in the cart
            if (flpOrderSummary.Controls.OfType<Panel>().Any())
            {
                // Subtotal
                flpOrderSummary.Controls.Add(new Label
                {
                    Text = $"Subtotal: ${subtotal:F2}",
                    AutoSize = true,
                    Tag = "Totals"
                });

                // Discount
                if (discountAmount > 0)
                {
                    flpOrderSummary.Controls.Add(new Label
                    {
                        Text = $"Discount Applied: -${discountAmount:F2}",
                        AutoSize = true,
                        ForeColor = Color.Green,
                        Font = new Font("Segoe UI", 9, FontStyle.Italic),
                        Tag = "Totals"
                    });
                }

                // Tax
                flpOrderSummary.Controls.Add(new Label
                {
                    Text = $"Tax (8.25%): ${taxAmount:F2}",
                    AutoSize = true,
                    Tag = "Totals"
                });

                // Final Total
                flpOrderSummary.Controls.Add(new Label
                {
                    Text = $"Final Total: ${finalTotal:F2}",
                    Font = new Font("Segoe UI", 10, FontStyle.Bold),
                    AutoSize = true,
                    Tag = "Totals"
                });

                // 🔹 Sync lblShowTotal with final total
                if (lblShowTotal != null)
                {
                    lblShowTotal.Text = $"${finalTotal:F2}";
                }
            }
            else
            {
                // If cart is empty, clear lblShowTotal
                if (lblShowTotal != null)
                {
                    lblShowTotal.Text = "$0.00";
                }
            }
        }

        public void AddItemToCart(string itemName, decimal cost, ItemData itemData)
        {
            // Check if item is already in cart, just increment quantity
            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
            {
                Label lbl = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Name:"));
                if (lbl != null && lbl.Text.Contains(itemName))
                {
                    TextBox existingQtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();
                    if (existingQtyBox != null && int.TryParse(existingQtyBox.Text, out int qty))
                    {
                        existingQtyBox.Text = (qty + 1).ToString();
                        return;
                    }
                }
            }

            // Create panel for new item
            Panel itemPanel = new Panel
            {
                Width = 400,
                Height = 100,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(5),
                Tag = itemData
            };

            // Create name and price labels
            Label nameLabel = new Label
            {
                Text = "Name: " + itemName,
                Location = new Point(10, 10),
                AutoSize = true
            };

            Label priceLabel = new Label
            {
                Text = "Price: $" + cost.ToString("F2"),
                Location = new Point(10, 30),
                AutoSize = true
            };

            Label qtyLabel = new Label
            {
                Text = "Qty:",
                Location = new Point(10, 55),
                AutoSize = true
            };

            TextBox qtyBox = new TextBox
            {
                Name = "qtyBox_" + itemName.Replace(" ", ""), // optional unique name
                Text = "1",
                Width = 40,
                Location = new Point(50, 52),
                Tag = itemData // Store original stock and info
            };

            // Auto-update stock and total when quantity changes
            qtyBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(qtyBox.Text, out int newQty))
                {
                    // If quantity is 0 or less, remove the panel
                    if (newQty <= 0)
                    {
                        itemPanel = qtyBox.Parent as Panel;
                        if (itemPanel != null && flpOrderSummary.Controls.Contains(itemPanel))
                        {
                            flpOrderSummary.Controls.Remove(itemPanel);
                            itemPanel.Dispose();
                        }

                        //Check if there are any items left in flpOrderSummary
                        bool hasItems = flpOrderSummary.Controls.OfType<Panel>().Any();

                        if (!hasItems)
                        {
                            // Remove discount label if it exists
                            Control discountLabel = flpOrderSummary.Controls
                                .OfType<Label>()
                                .FirstOrDefault(l => l.Name == "lblDiscountApplied");
                            if (discountLabel != null)
                            {
                                flpOrderSummary.Controls.Remove(discountLabel);
                                discountLabel.Dispose();
                            }

                            // Clear discount info
                            appliedDiscount = null;
                            appliedDiscountAmount = 0;

                            // Reset total display
                            if (lblShowTotal != null)
                                lblShowTotal.Text = "$0.00";

                            // Clear discount code text box
                            tbxAddDiscount.Text = string.Empty;

                            
                        }
                                          
                    


                    //Update totals after removing the item
                    UpdateTotal();
                        return;
                    }

                    // If quantity > 0, just update totals and stock info
                    if (qtyBox.Tag is ItemData data)
                    {
                        if (lblName.Text == data.Name)
                        {
                            int updatedStock = data.Stock - newQty;
                            lblCurrentStock.Text = $"In Stock: {updatedStock}";
                        }
                    }

                    // Update totals for any other changes
                    UpdateTotal();
                }
            };

            // Add controls to item panel
            itemPanel.Controls.Add(nameLabel);
            itemPanel.Controls.Add(priceLabel);
            itemPanel.Controls.Add(qtyLabel);
            itemPanel.Controls.Add(qtyBox);

            // Add item panel to order summary panel
            flpOrderSummary.Controls.Add(itemPanel);

            // Update total after adding
            UpdateTotal();
        }

        public int GetCartQuantity(string itemName)
        {
            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
            {
                Label nameLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.Contains(itemName));
                TextBox qtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();

                if (nameLabel != null && qtyBox != null && int.TryParse(qtyBox.Text, out int qty))
                {
                    return qty;
                }
            }
            return 0;
        }



        private void AddDiscountLabelToSummary(string code, decimal discountAmount)
        {
            // Remove any previous discount label
            Control existing = flpOrderSummary.Controls
                .OfType<Label>()
                .FirstOrDefault(l => l.Name == "lblDiscountApplied");

            if (existing != null)
                flpOrderSummary.Controls.Remove(existing);

            // Add new discount label
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


        private void btnAddDiscount_Click_1(object sender, EventArgs e)
        {
            string code = tbxAddDiscount.Text.Trim();

            // ✅ Check if there are any items in the cart
            if (!Validation.IsCartNotEmpty(flpOrderSummary, out string cartError))
            {
                MessageBox.Show(cartError, "Discount Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ✅ Require a discount code
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Please enter a discount code.");
                return;
            }

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(DBConnect.CONNECT_STRING))
                {
                    conn.Open();
                    using (SQLiteTransaction transaction = conn.BeginTransaction())
                    {
                        // 🔹 Lookup discount by code
                        DiscountInfo discount = DBConnect.GetDiscountInfoByCode(code);

                        if (discount == null)
                        {
                            MessageBox.Show("Invalid discount code.", "Discount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        decimal subtotal = 0;
                        decimal discountAmount = 0;

                        // 🔹 Calculate subtotal first
                        foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                        {
                            Label priceLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Price:"));
                            TextBox qtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();

                            if (priceLabel != null && qtyBox != null &&
                                decimal.TryParse(priceLabel.Text.Replace("Price: $", ""), out decimal price) &&
                                int.TryParse(qtyBox.Text, out int qty))
                            {
                                subtotal += price * qty;
                            }
                        }

                        // 🔹 APPLY DISCOUNTS BASED ON LEVEL & TYPE
                        if (discount.DiscountLevel == 0 && discount.DiscountType == 0 && discount.DiscountPercentage.HasValue)
                        {
                            // Cart-level percentage
                            discountAmount = subtotal * (discount.DiscountPercentage.Value / 100);
                        }
                        else if (discount.DiscountLevel == 0 && discount.DiscountType == 1 && discount.DiscountDollarAmount.HasValue)
                        {
                            // Cart-level dollar amount
                            discountAmount = discount.DiscountDollarAmount.Value;
                        }
                        else if (discount.DiscountLevel == 1 && discount.DiscountType == 0 && discount.DiscountPercentage.HasValue)
                        {
                            // Item-level percentage
                            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                            {
                                Label priceLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Price:"));
                                Label nameLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Name:"));
                                TextBox qtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();

                                if (priceLabel != null && nameLabel != null && qtyBox != null &&
                                    decimal.TryParse(priceLabel.Text.Replace("Price: $", ""), out decimal price) &&
                                    int.TryParse(qtyBox.Text, out int qty))
                                {
                                    string itemName = nameLabel.Text.Replace("Name: ", "");
                                    int? itemInventoryId = DBConnect.GetInventoryIDByName(itemName, conn, transaction);

                                    if (discount.InventoryID.HasValue && discount.InventoryID.Value == itemInventoryId)
                                    {
                                        discountAmount += (price * qty) * (discount.DiscountPercentage.Value / 100);
                                    }
                                }
                            }
                        }
                        else if (discount.DiscountLevel == 1 && discount.DiscountType == 1 && discount.DiscountDollarAmount.HasValue)
                        {
                            // Item-level dollar amount
                            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                            {
                                Label nameLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Name:"));
                                TextBox qtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();

                                if (nameLabel != null && qtyBox != null && int.TryParse(qtyBox.Text, out int qty))
                                {
                                    string itemName = nameLabel.Text.Replace("Name: ", "");
                                    int? itemInventoryId = DBConnect.GetInventoryIDByName(itemName, conn, transaction);

                                    if (discount.InventoryID.HasValue && discount.InventoryID.Value == itemInventoryId)
                                    {
                                        discountAmount += discount.DiscountDollarAmount.Value * qty;
                                    }
                                }
                            }
                        }
                        else
                        {
                            MessageBox.Show("Discount is not configured properly.", "Discount Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        // ✅ Store applied discount for later use
                        appliedDiscount = discount;
                        appliedDiscountAmount = discountAmount;

                        // ✅ Use your helper method to add the label
                        AddDiscountLabelToSummary(code, discountAmount);

                        transaction.Commit();
                    }
                }

                // ✅ Refresh total after applying discount
                UpdateTotal();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying discount: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void DisplayItemDetails(string name, decimal price, int stock, byte[] imageBytes, string description)
        {
            lblName.Text = name;
            lblPrice.Text = $"Price: ${price:F2}";
            lblCurrentStock.Text = $"In Stock: {stock}";
            lblDescription.Text = description;

            if (imageBytes != null)
            {
                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    pbxItemPicture.Image = Image.FromStream(ms);
                }
            }
            else
            {
                pbxItemPicture.Image = null;
            }
        }
            
        
        

        private void btnAll_Click(object sender, EventArgs e)
        {
            currentCategory = "All";
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), cbxFilter.SelectedItem?.ToString() ?? "Default");
        }

        private void btnRPG_Click(object sender, EventArgs e)
        {
            currentCategory = "RPG";
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), cbxFilter.SelectedItem?.ToString() ?? "Default");

        }

        private void btnMMO_Click(object sender, EventArgs e)
        {
            currentCategory = "MMO";
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), cbxFilter.SelectedItem?.ToString() ?? "Default");
        }

        private void btnShooter_Click(object sender, EventArgs e)
        {
            currentCategory = "Shooter";
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), cbxFilter.SelectedItem?.ToString() ?? "Default");
        }

        private void btnWorldBuilding_Click(object sender, EventArgs e)
        {
            currentCategory = "World Building";
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), cbxFilter.SelectedItem?.ToString() ?? "Default");
        }

        private void btnAction_Click(object sender, EventArgs e)
        {
            currentCategory = "Action";
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), cbxFilter.SelectedItem?.ToString() ?? "Default");
        }

        private void btnStrategy_Click(object sender, EventArgs e)
        {
            currentCategory = "Strategy";
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), cbxFilter.SelectedItem?.ToString() ?? "Default");
        }

        private void btnMOBA_Click(object sender, EventArgs e)
        {
            currentCategory = "MOBA";
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), cbxFilter.SelectedItem?.ToString() ?? "Default");
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            string searchTerm = tbxInputSearch.Text.Trim();

            InventoryDisplayHelper.DisplayItems(flpProducts, "All", searchTerm);
        }

        private void cbxFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedOption = cbxFilter.SelectedItem?.ToString() ?? "Default";
            string searchTerm = tbxInputSearch.Text.Trim();
            InventoryDisplayHelper.DisplayItems(flpProducts, currentCategory, tbxInputSearch.Text.Trim(), selectedOption);
        }

        private void btnConfirm_Click(object sender, EventArgs e)
        {

            if (!Validation.IsCartNotEmpty(flpOrderSummary, out string cartError))
            {
                MessageBox.Show(cartError, "Checkout Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string cardNumber = mtbCardInfo.Text.Trim();
            string ccv = mtbCCV.Text.Trim();
            string expDate = mtbExpDate.Text.Trim();

            if (!Validation.ValidateCheckoutFields(cardNumber, ccv, expDate, out string error))
            {
                MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool success = DBConnect.CheckoutAndInsertOrder(mtbCardInfo, mtbCCV, mtbExpDate, tbxAddDiscount, flpOrderSummary);

            if (success)
            {
                GenerateHtmlReceipt();
                
                flpOrderSummary.Controls.Clear();
                mtbCardInfo.Clear();
                mtbCCV.Clear();
                mtbExpDate.Clear();
                tbxAddDiscount.Text = "";
                appliedDiscount = null;
                appliedDiscountAmount = 0;

                // Reset total label
                if (lblShowTotal != null)
                    lblShowTotal.Text = "$0.00";
            }
        }


        private void GenerateHtmlReceipt()
        {
            string customerName = Validation.LoggedInCustomerUsername;
            string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            decimal subtotal = 0;
            decimal taxAmount = 0;
            decimal finalTotal = 0;
            decimal discountedSubtotal;

            StringBuilder html = new StringBuilder();

            html.AppendLine("<html><head><title>Order Receipt</title></head><body>");
            html.AppendLine($"<h1>Order Receipt</h1><p><strong>Customer:</strong> {customerName}</p>");
            html.AppendLine($"<p><strong>Date:</strong> {date}</p>");
            html.AppendLine("<table border='1' cellpadding='5'><tr><th>Item</th><th>Qty</th><th>Price</th><th>Subtotal</th></tr>");

            foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
            {
                string item = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Name:"))?.Text.Replace("Name: ", "") ?? "";
                string priceText = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Price:"))?.Text.Replace("Price: $", "") ?? "0";
                string qtyText = panel.Controls.OfType<TextBox>().FirstOrDefault()?.Text ?? "0";

                if (decimal.TryParse(priceText, out decimal price) && int.TryParse(qtyText, out int qty))
                {
                    decimal lineTotal = price * qty;
                    subtotal += lineTotal;
                    html.AppendLine($"<tr><td>{item}</td><td>{qty}</td><td>${price:F2}</td><td>${lineTotal:F2}</td></tr>");
                }
            }

            html.AppendLine("</table>");
            html.AppendLine($"<p><strong>Subtotal:</strong> ${subtotal:F2}</p>");

            discountedSubtotal = subtotal;

            if (appliedDiscount != null)
            {
                html.AppendLine($"<p><strong>Discount Code:</strong> {appliedDiscount.DiscountCode}</p>");

                if (appliedDiscount.DiscountType == 0 && appliedDiscount.DiscountPercentage.HasValue)
                {
                    appliedDiscountAmount = subtotal * (appliedDiscount.DiscountPercentage.Value / 100);
                    html.AppendLine($"<p><strong>Discount ({appliedDiscount.DiscountPercentage.Value}%):</strong> -${appliedDiscountAmount:F2}</p>");
                }
                else if (appliedDiscount.DiscountType == 1 && appliedDiscount.DiscountDollarAmount.HasValue)
                {
                    appliedDiscountAmount = appliedDiscount.DiscountDollarAmount.Value;
                    html.AppendLine($"<p><strong>Discount (${appliedDiscount.DiscountDollarAmount.Value}):</strong> -${appliedDiscountAmount:F2}</p>");
                }

                discountedSubtotal = subtotal - appliedDiscountAmount;
            }

            taxAmount = discountedSubtotal * 0.0825m;
            finalTotal = discountedSubtotal + taxAmount;

            html.AppendLine($"<p><strong>Tax (8.25%):</strong> ${taxAmount:F2}</p>");
            html.AppendLine($"<h3>Final Total: ${finalTotal:F2}</h3>");
            html.AppendLine("</body></html>");

            // Save to user's Documents/OneStopReceipts folder
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string receiptFolder = Path.Combine(documentsPath, "OneStopReceipts");

            Directory.CreateDirectory(receiptFolder);

            string fileName = $"Receipt_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            string filePath = Path.Combine(receiptFolder, fileName);

            try
            {
                File.WriteAllText(filePath, html.ToString());
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error generating or opening receipt:\n" + ex.Message);
            }
        }

        private void btnCheckout_Click(object sender, EventArgs e)
        {
            // ✅ Make sure the customer is logged in
            if (!Validation.IsCustomerLoggedIn())
            {
                MessageBox.Show("Please log in as a customer to complete the purchase.",
                                "Login Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ✅ Make sure there are items in the cart
            if (!Validation.IsCartNotEmpty(flpOrderSummary, out string cartError))
            {
                MessageBox.Show(cartError, "Checkout Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ✅ Validate payment fields before proceeding
            string cardNumber = mtbCardInfo.Text.Trim();
            string ccv = mtbCCV.Text.Trim();
            string expDate = mtbExpDate.Text.Trim();

            if (!Validation.ValidateCheckoutFields(cardNumber, ccv, expDate, out string error))
            {
                MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ✅ Perform checkout operations in database
            bool success = DBConnect.CheckoutAndInsertOrder(mtbCardInfo, mtbCCV, mtbExpDate, tbxAddDiscount, flpOrderSummary);

            if (success)
            {
                // ✅ Call UpdateTotal() to refresh totals (instead of recalculating here)
                UpdateTotal();

                // ✅ Generate the receipt BEFORE clearing
                GenerateHtmlReceipt();

                // ✅ Clear out the cart and reset everything for next order
                flpOrderSummary.Controls.Clear();
                mtbCardInfo.Clear();
                mtbCCV.Clear();
                mtbExpDate.Clear();
                tbxAddDiscount.Text = "";
                appliedDiscount = null;
                appliedDiscountAmount = 0;

                // ✅ Reset total label
                if (lblShowTotal != null)
                    lblShowTotal.Text = "$0.00";
            }
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            if (!Validation.IsCustomerLoggedIn())
                return;

            Validation.Logout();

            MessageBox.Show("You have been logged out.",
                "Logout", MessageBoxButtons.OK, MessageBoxIcon.Information);

            new frmMain().Show();
            this.Close();

        }
    }
}
