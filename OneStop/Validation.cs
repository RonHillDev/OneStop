using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OneStop
{
    internal class Validation
    {

        public static string LoggedInCustomerUsername { get; set; } = null;

        public static bool IsCustomerLoggedIn()
        {
            return !string.IsNullOrEmpty(LoggedInCustomerUsername);
        }

        public static string LoggedInManagerUsername { get; set; } = null;
        public static int? CurrentManagerSelectedCustomerId { get; set; }
        public static string CurrentManagerSelectedCustomerLogonName { get; set; }


        public static string LoggedInEmployeeUsername { get; set; } = null;
        public static int? CurrentEmployeeSelectedCustomerId { get; set; }
        public static string CurrentEmployeeSelectedCustomerLogonName { get; set; }

        public static bool IsManagerLoggedIn()
        {
            return !string.IsNullOrEmpty(LoggedInManagerUsername);
        }

        public static bool IsEmployeeLoggedIn()
        {
            return !string.IsNullOrEmpty(LoggedInEmployeeUsername);
        }

        public static void Logout()
        {
            // Clear who is logged in
            LoggedInCustomerUsername = null;
            LoggedInManagerUsername = null;
            LoggedInEmployeeUsername = null;

            // Clear manager POS selection
            CurrentManagerSelectedCustomerId = null;
            CurrentManagerSelectedCustomerLogonName = null;

            // Clear employee POS selection
            CurrentEmployeeSelectedCustomerId = null;
            CurrentEmployeeSelectedCustomerLogonName = null;
        }

        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return true; 

            if (email.Length > 40) return false;

            string pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            return Regex.IsMatch(email, pattern, RegexOptions.IgnoreCase);
        }

        public static bool IsValidPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return false;
            if (password.Length < 8 || password.Length > 20) return false;
            if (password.Any(char.IsWhiteSpace)) return false;

            bool hasUpper = password.Any(char.IsUpper);
            bool hasLower = password.Any(char.IsLower);
            bool hasDigit = password.Any(char.IsDigit);
            bool hasSpecial = password.Any(c => "()!@#$%^&*".Contains(c));

            int met = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
            return met >= 3;
        }


        public static bool ValidateInput(TextBox tbxEnterPass, TextBox tbxConfirmPass, ComboBox cbxTitle, ComboBox cbxSuffix, ComboBox cbxQuestions1,
            ComboBox cbxQuestions2, ComboBox cbxQuestions3, ComboBox cbxPosition, MaskedTextBox tbxPhone1, MaskedTextBox tbxPhone2, MaskedTextBox tbxZipcode,
            ComboBox cbxState)
        {
            string password = tbxEnterPass.Text;
            string confirmPassword = tbxConfirmPass.Text;

            if (password != confirmPassword)
            {
                MessageBox.Show("Passwords do not match.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!IsValidPassword(password))
            {
                MessageBox.Show("Password must be 8–20 characters and include at least one uppercase letter, one lowercase letter, one digit, one special character ()!@#$%^&*, and no spaces.",
                                "Invalid Password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (cbxTitle.SelectedIndex == -1 || cbxSuffix.SelectedIndex == -1 ||
                cbxQuestions1.SelectedIndex == -1 || cbxQuestions2.SelectedIndex == -1 ||
                cbxQuestions3.SelectedIndex == -1 || cbxPosition.SelectedIndex == -1)
            {
                MessageBox.Show("Please select all dropdown values before submitting.",
                                "Missing Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            tbxPhone1.TextMaskFormat = MaskFormat.ExcludePromptAndLiterals;
            string rawPhone1 = tbxPhone1.Text.Trim();

            if (!tbxPhone1.MaskFull || !Regex.IsMatch(rawPhone1, @"^\d{10}$"))
            {
                MessageBox.Show("Primary phone number must be exactly 10 digits.", "Phone Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            tbxPhone2.TextMaskFormat = MaskFormat.ExcludePromptAndLiterals;
            string rawPhone2 = tbxPhone2.Text.Trim();

            if (!string.IsNullOrWhiteSpace(rawPhone2) && (!tbxPhone2.MaskFull || !Regex.IsMatch(rawPhone2, @"^\d{10}$")))
            {
                MessageBox.Show("Secondary phone number must be 10 digits if entered.", "Phone Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!Regex.IsMatch(tbxZipcode.Text.Trim(), @"^\d{5}$"))
            {
                MessageBox.Show("Zip code must be exactly 5 digits.", "Zip Code Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (cbxState.SelectedIndex == -1)
            {
                MessageBox.Show("Please select a valid U.S. state.", "State Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        public static bool ValidateCheckoutFields(string cardNumber, string ccv, string expDate, out string errorMessage)
        {
            errorMessage = "";

            // Normalize: keep only digits
            cardNumber = GetDigits(cardNumber);
            ccv = GetDigits(ccv);
            string exp = expDate?.Replace("/", "").Replace("-", "").Trim() ?? "";

            // Card: 13–19 digits + Luhn
            if (cardNumber.Length < 13 || cardNumber.Length > 19)
            {
                errorMessage = "Please enter a valid credit card number.";
                return false;
            }

            // CVV: 3–4 digits
            if (!Regex.IsMatch(ccv, @"^\d{3,4}$"))
            {
                errorMessage = "Please enter a valid CVV (3 or 4 digits).";
                return false;
            }

            // Expiration: accept MMYY or MMYYYY (or masked MM/YY which becomes 4)
            exp = GetDigits(exp);
            if (exp.Length != 4 && exp.Length != 6)
            {
                errorMessage = "Expiration date must be MM/YY or MM/YYYY.";
                return false;
            }

            int mm, yy;
            try
            {
                if (exp.Length == 4)
                {
                    mm = int.Parse(exp.Substring(0, 2));
                    yy = 2000 + int.Parse(exp.Substring(2, 2));
                }
                else
                {
                    mm = int.Parse(exp.Substring(0, 2));
                    yy = int.Parse(exp.Substring(2, 4));
                }
            }
            catch
            {
                errorMessage = "Invalid expiration date.";
                return false;
            }

            if (mm < 1 || mm > 12)
            {
                errorMessage = "Expiration month must be 01–12.";
                return false;
            }

            var lastOfMonth = new DateTime(yy, mm, DateTime.DaysInMonth(yy, mm), 23, 59, 59);
            if (DateTime.Now > lastOfMonth)
            {
                errorMessage = "This card is expired.";
                return false;
            }

            return true;
        }

        private static string GetDigits(string s) =>
            string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsDigit).ToArray());

       

        public static bool IsCartNotEmpty(FlowLayoutPanel flpOrderSummary, out string errorMessage)
        {
            errorMessage = "";

            if (flpOrderSummary == null || flpOrderSummary.Controls.Count == 0)
            {
                errorMessage = "Your cart is empty. Please add items before checking out.";
                return false;
            }

            return true;
        }

        public static bool IsManagerCustomerSelected()
         => CurrentManagerSelectedCustomerId.HasValue
        && !string.IsNullOrWhiteSpace(CurrentManagerSelectedCustomerLogonName);


        public class OrderLine
        {
            public int InventoryID { get; set; }   
            public string ItemName { get; set; }  
            public int Qty { get; set; }          
            public decimal PriceEach { get; set; } 
            public decimal LineTotal => Math.Round(PriceEach * Qty, 2);
        }

        private static string StripPrefixIgnoreCase(string text, string prefix)
        {
            if (text == null) return string.Empty;
            if (prefix == null) return text;
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return text.Substring(prefix.Length).Trim();
            return text.Trim();
        }

        // Parse a label like "Price: $12.34" (case-insensitive, culture-safe)
        private static bool TryParsePriceFromLabel(Label priceLabel, out decimal price)
        {
            price = 0m;
            if (priceLabel == null || string.IsNullOrWhiteSpace(priceLabel.Text)) return false;

            string t = priceLabel.Text.Trim();

            // If it contains ":", take the part after it
            int colon = t.IndexOf(':');
            string after = colon >= 0 ? t.Substring(colon + 1) : t;

            after = after.Replace("$", "").Trim();

            // Try invariant first, then current culture
            if (decimal.TryParse(after, NumberStyles.Any, CultureInfo.InvariantCulture, out price))
                return true;

            return decimal.TryParse(after, NumberStyles.Any, CultureInfo.CurrentCulture, out price);
        }

        private static bool TryParseQtyFromTextBox(TextBox tb, out int qty)
        {
            qty = 0;
            if (tb == null) return false;
            return int.TryParse(tb.Text != null ? tb.Text.Trim() : "", out qty);
        }


        public static List<OrderLine> ExtractOrderLinesFromFlow(FlowLayoutPanel flp, out string errorMessage)
        {
            errorMessage = "";
            if (flp == null) { errorMessage = "Cart container was not found."; return null; }
            if (flp.Controls.Count == 0) { errorMessage = "Your cart is empty."; return null; }

            var lines = new List<OrderLine>();

            foreach (Control c in flp.Controls)
            {
                var p = c as Panel;
                if (p == null) continue;

                // 1) Prefer an OrderLine in Tag
                var olTag = p.Tag as OrderLine;
                if (olTag != null)
                {
                    if (olTag.InventoryID <= 0)
                    {
                        errorMessage = "One or more cart items are missing a valid InventoryID.";
                        return null;
                    }
                    if (olTag.Qty <= 0)
                    {
                        errorMessage = "Item quantities must be greater than 0.";
                        return null;
                    }

                    lines.Add(new OrderLine
                    {
                        InventoryID = olTag.InventoryID,
                        ItemName = olTag.ItemName,
                        Qty = olTag.Qty,
                        PriceEach = olTag.PriceEach
                    });
                    continue;
                }

                // 2) Or an ItemData in Tag (your provided class with Price)
                var item = p.Tag as ItemData;
                if (item != null)
                {
                    int qty = 1;
                    var qtyBox = p.Controls.OfType<TextBox>().FirstOrDefault();
                    if (qtyBox != null && !TryParseQtyFromTextBox(qtyBox, out qty)) qty = 1;
                    if (qty <= 0)
                    {
                        errorMessage = "Item quantities must be greater than 0.";
                        return null;
                    }

                    lines.Add(new OrderLine
                    {
                        InventoryID = item.InventoryID,
                        ItemName = item.Name,
                        Qty = qty,
                        PriceEach = item.Price
                    });
                    continue;
                }

                // 3) Fallback: parse labels "Name: ..." and "Price: ..."
                Label nameLabel = p.Controls.OfType<Label>().FirstOrDefault(l => l.Text != null && l.Text.Trim().StartsWith("Name:", StringComparison.OrdinalIgnoreCase));
                Label priceLabel = p.Controls.OfType<Label>().FirstOrDefault(l => l.Text != null && l.Text.Trim().StartsWith("Price:", StringComparison.OrdinalIgnoreCase));
                TextBox qtyBox2 = p.Controls.OfType<TextBox>().FirstOrDefault();

                if (nameLabel == null || priceLabel == null || qtyBox2 == null)
                {
                    errorMessage = "One or more cart items are missing fields (Name/Price/Qty).";
                    return null;
                }

                string itemName = StripPrefixIgnoreCase(nameLabel.Text, "Name:");
                decimal priceEach;
                if (!TryParsePriceFromLabel(priceLabel, out priceEach))
                {
                    errorMessage = "Invalid price detected for one of the items.";
                    return null;
                }

                int qtyParsed;
                if (!TryParseQtyFromTextBox(qtyBox2, out qtyParsed) || qtyParsed <= 0)
                {
                    errorMessage = "Item quantities must be valid numbers greater than 0.";
                    return null;
                }

                int inventoryId = 0;
                var tagged = qtyBox2.Tag as ItemData; // if you stashed ItemData here
                if (tagged != null) inventoryId = tagged.InventoryID;

                lines.Add(new OrderLine
                {
                    InventoryID = inventoryId,  // 0 if unknown; your DB layer can resolve by name if desired
                    ItemName = itemName,
                    Qty = qtyParsed,
                    PriceEach = priceEach
                });
            }

            return lines;
        }

        // Validation.cs
        public static bool ValidateOrderLines(
            List<OrderLine> lines,
            out string errorMessage,
            Func<int, int?> getQtyOnHand = null, // optional: inventoryId -> qty
            Func<int, decimal?> getCurrentPrice = null  // optional: inventoryId -> price
        )
        {
            errorMessage = "";
            if (lines == null || lines.Count == 0) { errorMessage = "Your cart is empty."; return false; }

            foreach (var l in lines)
            {
                if (l.InventoryID <= 0) { errorMessage = $"Item '{l.ItemName}' is missing a valid InventoryID."; return false; }
                if (l.Qty <= 0) { errorMessage = $"Quantity must be greater than 0 for '{l.ItemName}'."; return false; }
                if (l.PriceEach < 0) { errorMessage = $"Price cannot be negative for '{l.ItemName}'."; return false; }

                if (getQtyOnHand != null)
                {
                    var onHand = getQtyOnHand(l.InventoryID);
                    if (onHand.HasValue && l.Qty > onHand.Value)
                    {
                        errorMessage = $"Insufficient stock for '{l.ItemName}'. Available: {onHand.Value}, requested: {l.Qty}.";
                        return false;
                    }
                }

                if (getCurrentPrice != null)
                {
                    var dbPrice = getCurrentPrice(l.InventoryID);
                    if (dbPrice.HasValue && dbPrice.Value < 0)
                    {
                        errorMessage = $"Invalid price found in database for '{l.ItemName}'.";
                        return false;
                    }
                    // if you must enforce equality:
                    // if (dbPrice.HasValue && l.PriceEach != dbPrice.Value) { ... }
                }
            }

            if (lines.GroupBy(x => x.InventoryID).Any(g => g.Count() > 1))
            {
                errorMessage = "Duplicate items detected in the cart. Please consolidate quantities.";
                return false;
            }

            return true;
        }

        public static string SaveVerification(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            // Keep only digits
            string digits = new string(s.Where(char.IsDigit).ToArray());

            // This is where you can add *save-specific* logic if needed
            return digits;
        }

        // In Validation class
        public static bool ValidateNewPersonBasics(
            string logonName,
            string positionTitle,
            string email,
            string phonePrimary,
            string phoneSecondaryOptional,
            string zipcode,
            string state,
            out string error)
        {
            error = "";

            // username required (unique check happens in DB layer)
            if (string.IsNullOrWhiteSpace(logonName))
            {
                error = "Username (Logon Name) is required.";
                return false;
            }

            // position required
            if (string.IsNullOrWhiteSpace(positionTitle))
            {
                error = "Position Title is required.";
                return false;
            }

            // email optional but must pass format/length if provided
            if (!IsValidEmail(email))
            {
                error = "Invalid email (40 chars max, proper format).";
                return false;
            }

            // phones
            var p1 = SaveVerification(phonePrimary);
            if (p1.Length != 10)
            {
                error = "Primary phone must be exactly 10 digits.";
                return false;
            }

            var p2 = SaveVerification(phoneSecondaryOptional);
            if (!string.IsNullOrWhiteSpace(phoneSecondaryOptional) && p2.Length != 10)
            {
                error = "Secondary phone must be 10 digits if provided.";
                return false;
            }

            // zip
            if (!System.Text.RegularExpressions.Regex.IsMatch((zipcode ?? "").Trim(), @"^\d{5}$"))
            {
                error = "Zip code must be exactly 5 digits.";
                return false;
            }

            // state
            if (string.IsNullOrWhiteSpace(state))
            {
                error = "Please select a valid U.S. state.";
                return false;
            }

            return true;
        }


    }

}
