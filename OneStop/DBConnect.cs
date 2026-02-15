using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace OneStop
{
    internal class DBConnect
    {
        public const string CONNECT_STRING = @"Data Source=""C:\Users\ronhi\source\repos\OneStop\OneStop\bin\Debug\OneStop.db"";Version=3;Foreign Keys=True;";

        private static SQLiteConnection _cntDatabase;
        private static SQLiteCommand _sqlPersonCommand;
        private static SQLiteDataAdapter _daPerson = new SQLiteDataAdapter();


        private static DataTable _dtPersonTable = new DataTable();

        private static StringBuilder errorMessages = new StringBuilder();


        private static DataTable DTPersonTable
        {
            get { return _dtPersonTable; }
            set { _dtPersonTable = value; }

        }

        private static SQLiteCommand _sqlAdminCommand;

        private static SQLiteDataAdapter _dbAdmin = new SQLiteDataAdapter();

        private static DataTable _dbAdminTable = new DataTable();

        private static DataTable DBAdminTable
        {
            get { return _dbAdminTable; }
            set { _dbAdminTable = value; }
        }

        public class DiscountInfo
        {
            public int DiscountID { get; set; }
            public int DiscountLevel { get; set; } // 0 = cart, 1 = item
            public int? InventoryID { get; set; }
            public int DiscountType { get; set; }  // 0 = percent, 1 = dollar
            public decimal? DiscountPercentage { get; set; }
            public decimal? DiscountDollarAmount { get; set; }
            public string DiscountCode { get; set; }
        }

        public static void OpenDatabase()
        {
            try
            {
                if (_cntDatabase != null && _cntDatabase.State == ConnectionState.Open)
                    return;

                _cntDatabase = new SQLiteConnection(CONNECT_STRING);
                _cntDatabase.Open();
            }
            catch (SQLiteException ex)
            {
                MessageBox.Show("SQLite error opening database:\n" + ex.Message,
                    "Open Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);


                try { _cntDatabase?.Dispose(); } catch { }
                _cntDatabase = null;
            }
        }

        public static void QuestionsBox(ComboBox cbxQuestions1, ComboBox cbxQuestions2, ComboBox cbxQuestions3)
        {

            try
            {
                OpenDatabase();

                using (SQLiteTransaction transaction = _cntDatabase.BeginTransaction())
                {

                    string insertQuestion1 = "SELECT QuestionID, QuestionPrompt FROM SecurityQuestions WHERE SetID = 1";

                    SQLiteCommand cmdQuestion1 = new SQLiteCommand(insertQuestion1, _cntDatabase, transaction);
                    SQLiteDataAdapter adapter1 = new SQLiteDataAdapter(cmdQuestion1);

                    DataTable dataTable1 = new DataTable();
                    adapter1.Fill(dataTable1);
                    cbxQuestions1.DataSource = dataTable1;
                    cbxQuestions1.DisplayMember = "QuestionPrompt";
                    cbxQuestions1.ValueMember = "QuestionID";

                    string insertQuestion2 = "SELECT QuestionID, QuestionPrompt FROM SecurityQuestions WHERE SetID = 2";

                    SQLiteCommand cmdQuestion2 = new SQLiteCommand(insertQuestion2, _cntDatabase, transaction);
                    SQLiteDataAdapter adapter2 = new SQLiteDataAdapter(cmdQuestion2);

                    DataTable dataTable2 = new DataTable();
                    adapter2.Fill(dataTable2);

                    cbxQuestions2.DataSource = dataTable2;
                    cbxQuestions2.DisplayMember = "QuestionPrompt";
                    cbxQuestions2.ValueMember = "QuestionID";

                    string insertQuestion3 = "SELECT QuestionID, QuestionPrompt FROM SecurityQuestions WHERE SetID = 3";

                    SQLiteCommand cmdQuestion3 = new SQLiteCommand(insertQuestion3, _cntDatabase, transaction);
                    SQLiteDataAdapter adapter3 = new SQLiteDataAdapter(cmdQuestion3);

                    DataTable dataTable3 = new DataTable();
                    adapter3.Fill(dataTable3);

                    cbxQuestions3.DataSource = dataTable3;
                    cbxQuestions3.DisplayMember = "QuestionPrompt";
                    cbxQuestions3.ValueMember = "QuestionID";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during insert:\n" + ex.Message, "Options Not Loading", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
        }

        public static void InsertNewRecord(
                TextBox tbxEnterPass, TextBox tbxConfirmPass, TextBox tbxFirstName, TextBox tbxMiddleName, TextBox tbxLastName,
                TextBox tbxAddress, TextBox tbxCity, MaskedTextBox tbxZipcode,
                TextBox tbxAnswer1, TextBox tbxAnswer2, TextBox tbxAnswer3,
                ComboBox cbxTitle, ComboBox cbxSuffix,
                ComboBox cbxQuestions1, ComboBox cbxQuestions2, ComboBox cbxQuestions3,
                ComboBox cbxPosition, ComboBox cbxState,
                TextBox tbxAddress2, TextBox tbxAddress3, TextBox tbxEmail,
                MaskedTextBox tbxPhone1, MaskedTextBox tbxPhone2,
                TextBox tbxEnterUser)
        {
            if (string.IsNullOrWhiteSpace(tbxFirstName.Text) ||
                string.IsNullOrWhiteSpace(tbxLastName.Text) ||
                string.IsNullOrWhiteSpace(tbxAddress.Text) ||
                string.IsNullOrWhiteSpace(tbxCity.Text) ||
                string.IsNullOrWhiteSpace(tbxZipcode.Text) ||
                string.IsNullOrWhiteSpace(tbxAnswer1.Text) ||
                string.IsNullOrWhiteSpace(tbxAnswer2.Text) ||
                string.IsNullOrWhiteSpace(tbxAnswer3.Text))
            {
                MessageBox.Show(
                    "Please fill in all required fields:\nFirst Name, Last Name, Address, City, Zipcode, and all three security answers.",
                    "Missing Information", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                OpenDatabase();

                using (SQLiteTransaction transaction = _cntDatabase.BeginTransaction())
                {
                    try
                    {
                        // 1) Resolve PositionID safely
                        int positionIDTemp;
                        const string positionLookUp = "SELECT PositionID FROM Position WHERE PositionTitle = @Title;";
                        using (var cmdPosition = new SQLiteCommand(positionLookUp, _cntDatabase, transaction))
                        {
                            cmdPosition.Parameters.AddWithValue("@Title", cbxPosition.Text);
                            object posObj = cmdPosition.ExecuteScalar();
                            if (posObj == null || posObj == DBNull.Value)
                                throw new Exception("Invalid Position selected.");

                            positionIDTemp = Convert.ToInt32(posObj);
                        }

                        // 2) Insert Person + get new PersonID
                        const string insertPersonQuery = @"
                        INSERT INTO Person
                        (Title, NameFirst, NameMiddle, NameLast, Suffix, Address1, Address2, Address3, City, Zipcode, State, Email, PhonePrimary, PhoneSecondary, PositionID)
                        VALUES
                        (@Title, @NameFirst, @NameMiddle, @NameLast, @Suffix, @Address1, @Address2, @Address3, @City, @Zipcode, @State, @Email, @PhonePrimary, @PhoneSecondary, @PositionID);
                        SELECT last_insert_rowid();";

                        int personID;
                        using (var cmdPerson = new SQLiteCommand(insertPersonQuery, _cntDatabase, transaction))
                        {
                            cmdPerson.Parameters.AddWithValue("@Title", cbxTitle.Text?.Trim() ?? "");
                            cmdPerson.Parameters.AddWithValue("@NameFirst", tbxFirstName.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@NameMiddle",
                                string.IsNullOrWhiteSpace(tbxMiddleName.Text) ? (object)DBNull.Value : tbxMiddleName.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@NameLast", tbxLastName.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@Suffix",
                                string.IsNullOrWhiteSpace(cbxSuffix.Text) ? (object)DBNull.Value : cbxSuffix.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@Address1", tbxAddress.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@Address2",
                                string.IsNullOrWhiteSpace(tbxAddress2.Text) ? (object)DBNull.Value : tbxAddress2.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@Address3",
                                string.IsNullOrWhiteSpace(tbxAddress3.Text) ? (object)DBNull.Value : tbxAddress3.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@City", tbxCity.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@Zipcode", tbxZipcode.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@State", cbxState.Text?.Trim() ?? "");
                            cmdPerson.Parameters.AddWithValue("@Email",
                                string.IsNullOrWhiteSpace(tbxEmail.Text) ? (object)DBNull.Value : tbxEmail.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@PhonePrimary", tbxPhone1.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@PhoneSecondary",
                                string.IsNullOrWhiteSpace(tbxPhone2.Text) ? (object)DBNull.Value : tbxPhone2.Text.Trim());
                            cmdPerson.Parameters.AddWithValue("@PositionID", positionIDTemp);

                            personID = Convert.ToInt32(cmdPerson.ExecuteScalar());
                        }

                        // 3) Insert Logon
                        const string insertLogonQuery = @"
                        INSERT INTO Logon
                        (PersonID, LogonName, Password, FirstChallengeQuestion, FirstChallengeAnswer,
                         SecondChallengeQuestion, SecondChallengeAnswer, ThirdChallengeQuestion, ThirdChallengeAnswer, PositionTitle)
                        VALUES
                        (@PersonID, @LogonName, @Password, @FirstChallengeQuestion, @FirstChallengeAnswer,
                         @SecondChallengeQuestion, @SecondChallengeAnswer, @ThirdChallengeQuestion, @ThirdChallengeAnswer, @PositionTitle);";

                        using (var cmdLogon = new SQLiteCommand(insertLogonQuery, _cntDatabase, transaction))
                        {
                            cmdLogon.Parameters.AddWithValue("@PersonID", personID);
                            cmdLogon.Parameters.AddWithValue("@LogonName", tbxEnterUser.Text.Trim());
                            cmdLogon.Parameters.AddWithValue("@Password", tbxEnterPass.Text);
                            cmdLogon.Parameters.AddWithValue("@FirstChallengeQuestion", cbxQuestions1.SelectedValue);
                            cmdLogon.Parameters.AddWithValue("@FirstChallengeAnswer", tbxAnswer1.Text.Trim());
                            cmdLogon.Parameters.AddWithValue("@SecondChallengeQuestion", cbxQuestions2.SelectedValue);
                            cmdLogon.Parameters.AddWithValue("@SecondChallengeAnswer", tbxAnswer2.Text.Trim());
                            cmdLogon.Parameters.AddWithValue("@ThirdChallengeQuestion", cbxQuestions3.SelectedValue);
                            cmdLogon.Parameters.AddWithValue("@ThirdChallengeAnswer", tbxAnswer3.Text.Trim());
                            cmdLogon.Parameters.AddWithValue("@PositionTitle", cbxPosition.Text.Trim());

                            cmdLogon.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        MessageBox.Show("Registration Successful", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception exInner)
                    {
                        try { transaction.Rollback(); } catch { }

                        if (exInner is SQLiteException sqlEx && sqlEx.ResultCode == SQLiteErrorCode.Constraint)
                        {
                            MessageBox.Show("That username already exists. Please choose a different one.",
                                "Duplicate Username", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        else
                        {
                            MessageBox.Show("Error during insert:\n" + exInner.Message,
                                "Transaction Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception exOuter)
            {
                MessageBox.Show("Could not connect or begin transaction:\n" + exOuter.Message,
                    "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
        }


        public static (string, string, string) GetSecurityPrompts(string logonName)
        {
            string prompt1 = "", prompt2 = "", prompt3 = "";

            try
            {
                OpenDatabase();

                string query = @"
            SELECT 
                (SELECT QuestionPrompt FROM SecurityQuestions 
                 WHERE QuestionID = Logon.FirstChallengeQuestion) AS FirstPrompt,
                (SELECT QuestionPrompt FROM SecurityQuestions 
                 WHERE QuestionID = Logon.SecondChallengeQuestion) AS SecondPrompt,
                (SELECT QuestionPrompt FROM SecurityQuestions 
                 WHERE QuestionID = Logon.ThirdChallengeQuestion) AS ThirdPrompt
            FROM Logon
            WHERE LogonName = @LogonName";

                using (SQLiteCommand cmd = new SQLiteCommand(query, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@LogonName", logonName);

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            prompt1 = reader["FirstPrompt"].ToString();
                            prompt2 = reader["SecondPrompt"].ToString();
                            prompt3 = reader["ThirdPrompt"].ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error retrieving prompts:\n" + ex.Message,
                                "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return (prompt1, prompt2, prompt3);
        }

        public static bool ValidateSecurityAnswers(string logonName, string answer1, string answer2, string answer3)
        {
            try
            {
                OpenDatabase();

                string query = @"SELECT FirstChallengeAnswer, SecondChallengeAnswer, ThirdChallengeAnswer
                         FROM Logon
                         WHERE LogonName = @LogonName";

                using (SQLiteCommand cmd = new SQLiteCommand(query, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@LogonName", logonName);

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string dbAnswer1 = reader["FirstChallengeAnswer"].ToString();
                            string dbAnswer2 = reader["SecondChallengeAnswer"].ToString();
                            string dbAnswer3 = reader["ThirdChallengeAnswer"].ToString();

                            return answer1.Equals(dbAnswer1, StringComparison.OrdinalIgnoreCase) &&
                                   answer2.Equals(dbAnswer2, StringComparison.OrdinalIgnoreCase) &&
                                   answer3.Equals(dbAnswer3, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error validating answers:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }

            return false;
        }

        public static bool ResetPassword(string logonName, string newPassword)
        {
            try
            {
                OpenDatabase();

                string query = @"UPDATE Logon
                         SET Password = @Password
                         WHERE LogonName = @LogonName";

                using (SQLiteCommand cmd = new SQLiteCommand(query, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@Password", newPassword);
                    cmd.Parameters.AddWithValue("@LogonName", logonName);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error resetting password:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }

            return false;
        }

        public static bool LoginUser(string username, string password, out string positionTitle)
        {
            positionTitle = string.Empty;

            try
            {
                OpenDatabase();

                const string sql = @"
                    SELECT PositionTitle
                    FROM Logon
                    WHERE LogonName = @Username COLLATE BINARY
                      AND Password  = @Password  COLLATE BINARY
                    LIMIT 1;";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@Username", (username ?? "").Trim());
                    cmd.Parameters.AddWithValue("@Password", (password ?? "").Trim());

                    object result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        positionTitle = result.ToString();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during login:\n" + ex.Message,
                    "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }

            return false;
        }


        public static string GetUserRole(string username)
        {
            string role = "";

            try
            {
                OpenDatabase();

                string query = @"SELECT PositionTitle
                         FROM Logon
                         WHERE LogonName = @Username";

                using (SQLiteCommand cmd = new SQLiteCommand(query, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@Username", username);
                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        role = result.ToString();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error retrieving user role:\n" + ex.Message);
            }
            finally
            {
                CloseDatabase();
            }

            return role;
        }

        public static class InventoryDisplayHelper
        {
            public static void DisplayItems(FlowLayoutPanel flpProducts, string categoryFilter = "All", string searchTerm = "", string sortOption = "")
            {
                flpProducts.Controls.Clear();

                try
                {
                    OpenDatabase();

                    string query = @"
                            SELECT 
                            i.ItemName, 
                            i.Cost, 
                            c.CategoryName, 
                            i.Quantity, 
                            i.ItemImage,
                            i.ItemDescription
                            FROM 
                            Inventory i
                            JOIN 
                            Categories c ON i.CategoryID = c.CategoryID";

                    List<string> filters = new List<string>();
                    SQLiteCommand cmd = new SQLiteCommand();
                    cmd.Connection = _cntDatabase;

                    // Always exclude discontinued
                    filters.Add("i.Discontinued = 0");

                    if (!string.Equals(categoryFilter, "All", StringComparison.OrdinalIgnoreCase))
                    {
                        filters.Add("c.CategoryName = @CategoryName");
                        cmd.Parameters.AddWithValue("@CategoryName", categoryFilter);
                    }

                    if (!string.IsNullOrWhiteSpace(searchTerm))
                    {
                        filters.Add("(i.ItemName LIKE @Search OR i.ItemDescription LIKE @Search)");
                        cmd.Parameters.AddWithValue("@Search", $"%{searchTerm}%");
                    }

                    if (filters.Count > 0)
                    {
                        query += " WHERE " + string.Join(" AND ", filters);
                    }

                    switch (sortOption)
                    {
                        case "Price: Low to High":
                            query += " ORDER BY i.Cost ASC";
                            break;
                        case "Price: High to Low":
                            query += " ORDER BY i.Cost DESC";
                            break;
                        case "Stock: Low to High":
                            query += " ORDER BY i.Quantity ASC";
                            break;
                        case "Stock: High to Low":
                            query += " ORDER BY i.Quantity DESC";
                            break;
                        case "Default":
                        default:
                            break;
                    }

                    cmd.CommandText = query;

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string itemName = reader["ItemName"].ToString();
                            decimal cost = Convert.ToDecimal(reader["Cost"]);
                            string category = reader["CategoryName"].ToString();
                            int quantity = Convert.ToInt32(reader["Quantity"]);
                            byte[] imageBytes = reader["ItemImage"] != DBNull.Value ? (byte[])reader["ItemImage"] : null;
                            string description = reader["ItemDescription"].ToString();

                            ItemData itemData = new ItemData
                            {
                                Name = itemName,
                                Price = cost,
                                Stock = quantity,
                                Description = description,
                                ImageBytes = imageBytes
                            };

                            Panel itemPanel = new Panel
                            {
                                Width = 200,
                                Height = 290,
                                BorderStyle = BorderStyle.FixedSingle,
                                Margin = new Padding(10),
                                Tag = itemData
                            };

                            PictureBox picture = new PictureBox
                            {
                                Width = 180,
                                Height = 120,
                                SizeMode = PictureBoxSizeMode.StretchImage,
                                Image = imageBytes != null ? ByteArrayToImage(imageBytes) : null
                            };

                            Label nameLabel = new Label { Text = "Name: " + itemName, AutoSize = true };
                            Label costLabel = new Label { Text = "Cost: $" + cost.ToString("F2"), AutoSize = true };
                            Label categoryLabel = new Label { Text = "Category: " + category, AutoSize = true };
                            Label quantityLabel = new Label { Text = "Quantity: " + quantity, AutoSize = true };

                            Button viewButton = new Button
                            {
                                Text = "View",
                                Width = 80,
                                Tag = itemData
                            };

                            viewButton.Click += (s, e) =>
                            {
                                if (viewButton.Tag is ItemData data)
                                {
                                    Form activeForm = Application.OpenForms["CustomerView"];
                                    if (activeForm is CustomerView customerView)
                                    {
                                        customerView.DisplayItemDetails(
                                            data.Name,
                                            data.Price,
                                            data.Stock,
                                            data.ImageBytes,
                                            data.Description
                                        );
                                    }
                                }
                            };

                            Button buyButton = new Button
                            {
                                Text = "Buy",
                                Width = 80,
                                Tag = itemData
                            };

                            buyButton.Click += (s, e) =>
                            {
                                if (buyButton.Tag is ItemData data)
                                {
                                    Form activeForm = Application.OpenForms["CustomerView"];
                                    if (activeForm is CustomerView customerView)
                                    {
                                        customerView.AddItemToCart(data.Name, data.Price, data);

                                        if (customerView.LblName.Text == data.Name)
                                        {
                                            int cartQty = customerView.GetCartQuantity(data.Name);
                                            int updatedStock = data.Stock - cartQty;
                                            customerView.LblCurrentStock.Text = $"In Stock: {updatedStock}";
                                        }
                                    }
                                }
                            };

                            // Add controls to panel
                            itemPanel.Controls.Add(picture);
                            itemPanel.Controls.Add(nameLabel);
                            itemPanel.Controls.Add(costLabel);
                            itemPanel.Controls.Add(categoryLabel);
                            itemPanel.Controls.Add(quantityLabel);
                            itemPanel.Controls.Add(viewButton);
                            itemPanel.Controls.Add(buyButton);

                            // Position controls
                            nameLabel.Location = new Point(10, picture.Bottom + 5);
                            costLabel.Location = new Point(10, nameLabel.Bottom + 5);
                            categoryLabel.Location = new Point(10, costLabel.Bottom + 5);
                            quantityLabel.Location = new Point(10, categoryLabel.Bottom + 5);
                            viewButton.Location = new Point(10, quantityLabel.Bottom + 5);
                            buyButton.Location = new Point(100, quantityLabel.Bottom + 5);

                            flpProducts.Controls.Add(itemPanel);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading items: " + ex.Message);
                }
            }

        }


        private static Image ByteArrayToImage(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                return Image.FromStream(ms);
            }
        }


        public static decimal AddDiscount(string code, out bool isPercentage)
        {
            decimal discountValue = 0;
            isPercentage = true;

            string query = @"
        SELECT DiscountType, DiscountPercentage, DiscountDollarAmount 
        FROM Discounts 
        WHERE DiscountCode = @Code";

            using (SQLiteConnection conn = new SQLiteConnection(CONNECT_STRING))
            {
                conn.Open();
                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Code", code);

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int discountType = reader.GetInt32(0);

                            if (discountType == 0 && !reader.IsDBNull(1))
                            {
                                discountValue = reader.GetDecimal(1); // Percentage
                                isPercentage = true;
                            }
                            else if (discountType == 1 && !reader.IsDBNull(2))
                            {
                                discountValue = reader.GetDecimal(2); // Flat amount
                                isPercentage = false;
                            }
                        }
                    }
                }
            }

            return discountValue;
        }

        public static bool CheckoutAndInsertOrder(MaskedTextBox mtbCardInfo, MaskedTextBox mtbCCV, MaskedTextBox mtbExpDate, TextBox tbxAddDiscount, FlowLayoutPanel flpOrderSummary)
        {
            // Figure out who the CUSTOMER is (PersonID) and who the STAFF user is (EmployeeID)
            int? personId = null;     // the Customer's PersonID
            int? employeeId = null;   // the Manager/Employee PersonID (nullable for self-checkout)

            try
            {
                // POS (Manager/Employee) flow
                if (Validation.IsManagerLoggedIn())
                {
                    if (!Validation.IsManagerCustomerSelected() || !Validation.CurrentManagerSelectedCustomerId.HasValue)
                    {
                        MessageBox.Show("Please select a customer before checking out.",
                                        "Customer Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }

                    // Customer = selected row in dgvCustomers
                    personId = Validation.CurrentManagerSelectedCustomerId.Value;

                    // Staff = the logged-in manager/employee
                    if (string.IsNullOrWhiteSpace(Validation.LoggedInManagerUsername))
                    {
                        MessageBox.Show("Could not resolve the current staff user.", "Login Required",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }

                    string staffPersonIdStr = DBConnect.GetPersonIDByUsername(Validation.LoggedInManagerUsername);
                    if (!int.TryParse(staffPersonIdStr, out int staffPid))
                    {
                        MessageBox.Show("Could not resolve the current staff user (PersonID).", "Login Required",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    employeeId = staffPid;
                }
                else
                {
                    // Customer self-checkout flow
                    if (!Validation.IsCustomerLoggedIn())
                    {
                        MessageBox.Show("Please log in to complete the order.", "Login Required",
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }

                    string pidStr = DBConnect.GetPersonIDByUsername(Validation.LoggedInCustomerUsername);
                    if (!int.TryParse(pidStr, out int custPid))
                    {
                        MessageBox.Show("Could not retrieve user info.", "Error",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    personId = custPid;

                    // No staff on self-checkout
                    employeeId = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error resolving customer/staff:\n" + ex.Message, "Order Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            // Get payment + discount inputs (as you had)
            string cardInfo = mtbCardInfo.Text.Trim();
            string ccv = mtbCCV.Text.Trim();
            string expDate = mtbExpDate.Text.Trim();
            string discountCode = tbxAddDiscount.Text.Trim();
            int? discountId = null;

            if (!string.IsNullOrEmpty(discountCode))
            {
                DiscountInfo di = DBConnect.GetDiscountInfoByCode(discountCode);
                discountId = di?.DiscountID;
            }

            using (SQLiteConnection conn = new SQLiteConnection(CONNECT_STRING))
            {
                conn.Open();
                SQLiteTransaction transaction = conn.BeginTransaction();

                try
                {
                    // 1) PRE-CHECK STOCK
                    foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                    {
                        Label nameLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Name:"));
                        TextBox qtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();

                        if (nameLabel != null && qtyBox != null && int.TryParse(qtyBox.Text, out int qty))
                        {
                            string itemName = nameLabel.Text.Replace("Name: ", "");
                            int inventoryId = DBConnect.GetInventoryIDByName(itemName, conn, transaction);
                            int currentStock = DBConnect.GetInventoryStock(inventoryId, conn, transaction);

                            if (currentStock < qty)
                            {
                                MessageBox.Show($"Not enough stock for {itemName}. Available: {currentStock}",
                                                "Stock Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                transaction.Rollback();
                                return false;
                            }
                        }
                    }

                    // 2) INSERT into Orders (adds EmployeeID)
                    string insertOrderQuery = @"
                    INSERT INTO Orders
                        (PersonID, EmployeeID, OrderDate, CC_Number, CCV, ExpDate, DiscountID)
                    VALUES
                        (@PersonID, @EmployeeID, @OrderDate, @CC_Number, @CCV, @ExpDate, @DiscountID);
                    SELECT last_insert_rowid();";

                    SQLiteCommand cmdOrder = new SQLiteCommand(insertOrderQuery, conn, transaction);
                    cmdOrder.Parameters.AddWithValue("@PersonID", personId.Value);
                    cmdOrder.Parameters.AddWithValue("@EmployeeID", (object)employeeId ?? DBNull.Value);  // nullable
                    cmdOrder.Parameters.AddWithValue("@OrderDate", DateTime.Now);
                    cmdOrder.Parameters.AddWithValue("@CC_Number", cardInfo);
                    cmdOrder.Parameters.AddWithValue("@CCV", ccv);
                    cmdOrder.Parameters.AddWithValue("@ExpDate", expDate);
                    cmdOrder.Parameters.AddWithValue("@DiscountID", (object)discountId ?? DBNull.Value);

                    int orderId = Convert.ToInt32(cmdOrder.ExecuteScalar());

                    // 3) INSERT OrderDetails + UPDATE Inventory
                    foreach (Panel panel in flpOrderSummary.Controls.OfType<Panel>())
                    {
                        Label nameLabel = panel.Controls.OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Name:"));
                        TextBox qtyBox = panel.Controls.OfType<TextBox>().FirstOrDefault();
                        if (nameLabel == null || qtyBox == null) continue;

                        string itemName = nameLabel.Text.Replace("Name: ", "");
                        if (!int.TryParse(qtyBox.Text, out int quantity)) quantity = 0;

                        int inventoryId = DBConnect.GetInventoryIDByName(itemName, conn, transaction);

                        string insertDetailsQuery = @"
                        INSERT INTO OrderDetails
                            (OrderID, InventoryID, Quantity, DiscountID)
                        VALUES
                            (@OrderID, @InventoryID, @Quantity, @DiscountID);";

                        using (SQLiteCommand cmdDetail = new SQLiteCommand(insertDetailsQuery, conn, transaction))
                        {
                            cmdDetail.Parameters.AddWithValue("@OrderID", orderId);
                            cmdDetail.Parameters.AddWithValue("@InventoryID", inventoryId);
                            cmdDetail.Parameters.AddWithValue("@Quantity", quantity);
                            cmdDetail.Parameters.AddWithValue("@DiscountID", (object)discountId ?? DBNull.Value);
                            cmdDetail.ExecuteNonQuery();
                        }

                        string updateStockQuery = @"
                        UPDATE Inventory
                        SET Quantity = Quantity - @Quantity
                        WHERE InventoryID = @InventoryID;";

                        using (SQLiteCommand cmdStock = new SQLiteCommand(updateStockQuery, conn, transaction))
                        {
                            cmdStock.Parameters.AddWithValue("@Quantity", quantity);
                            cmdStock.Parameters.AddWithValue("@InventoryID", inventoryId);
                            cmdStock.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                    MessageBox.Show("Order completed successfully!", "Success",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return true;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    MessageBox.Show("An error occurred while processing the order: " + ex.Message,
                                    "Order Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
        }


        public static int GetInventoryStock(int inventoryId, SQLiteConnection conn, SQLiteTransaction tx)
        {
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT Quantity FROM Inventory WHERE InventoryID = @InventoryID", conn, tx))
            {
                cmd.Parameters.AddWithValue("@InventoryID", inventoryId);
                object result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }


        public static string GetPersonIDByUsername(string username)
        {
            using (SQLiteConnection conn = new SQLiteConnection(CONNECT_STRING))
            {
                string query = "SELECT PersonID FROM Logon WHERE LogonName = @LogonName";
                SQLiteCommand cmd = new SQLiteCommand(query, conn);
                cmd.Parameters.AddWithValue("@LogonName", username);

                conn.Open();
                object result = cmd.ExecuteScalar();
                return result?.ToString();
            }
        }



        public static DiscountInfo GetDiscountInfoByCode(string code)
        {
            using (SQLiteConnection conn = new SQLiteConnection(CONNECT_STRING))
            {

                string query = @"
                SELECT
                    DiscountID,
                    DiscountCode,
                    DiscountLevel,
                    InventoryID,
                    DiscountType,
                    DiscountPercentage,
                    DiscountDollarAmount
                FROM Discounts
                WHERE DiscountCode = @DiscountCode
                  AND (StartDate IS NULL OR date(StartDate) <= date(@Today))
                  AND (ExpirationDate IS NULL OR date(ExpirationDate) >= date(@Today));";


                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@DiscountCode", code ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Today", DateTime.Today.ToString("yyyy-MM-dd"));


                    conn.Open();
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new DiscountInfo
                            {
                                DiscountID = (int)reader["DiscountID"],
                                DiscountCode = reader["DiscountCode"].ToString(),
                                DiscountLevel = (int)reader["DiscountLevel"],
                                InventoryID = reader["InventoryID"] == DBNull.Value ? (int?)null : (int)reader["InventoryID"],
                                DiscountType = (int)reader["DiscountType"],
                                DiscountPercentage = reader["DiscountPercentage"] == DBNull.Value ? (decimal?)null : (decimal)reader["DiscountPercentage"],
                                DiscountDollarAmount = reader["DiscountDollarAmount"] == DBNull.Value ? (decimal?)null : (decimal)reader["DiscountDollarAmount"]
                            };
                        }
                    }
                }
            }
            return null;
        }

        public static int GetInventoryIDByName(string itemName, SQLiteConnection conn, SQLiteTransaction transaction)
        {
            string query = "SELECT InventoryID FROM Inventory WHERE ItemName = @ItemName";
            using (SQLiteCommand cmd = new SQLiteCommand(query, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@ItemName", itemName);
                object result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : -1;
            }
        }

        public static int GetInventoryIDByName(string itemName)
        {
            try
            {
                OpenDatabase();
                string query = "SELECT InventoryID FROM Inventory WHERE ItemName = @ItemName";
                using (SQLiteCommand cmd = new SQLiteCommand(query, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@ItemName", itemName);
                    object result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : -1;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error looking up InventoryID:\n" + ex.Message, "Database Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return -1;
            }
            finally
            {
                CloseDatabase();
            }
        }

        public static DataTable SearchInventoryProducts(string term)
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();

                string sql = @"
        SELECT 
            InventoryID,
            ItemName,
            RetailPrice,     
            Quantity,
            ItemDescription,
            ItemImage
        FROM Inventory
        WHERE Discontinued = 0
          AND (
                @t = '' 
                OR ItemName LIKE '%' + @t + '%'
                OR ItemDescription LIKE '%' + @t + '%'
              )
        ORDER BY ItemName;";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@t", term ?? string.Empty);
                    using (var da = new SQLiteDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error searching inventory:\n" + ex.Message, "Database Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
            return dt;
        }


        public static bool IsDiscountValidForItem(string discountCode, int inventoryId)
        {
            using (SQLiteConnection conn = new SQLiteConnection(CONNECT_STRING))
            {
                conn.Open();
                string query = @"SELECT COUNT(*) FROM Discounts 
                         WHERE DiscountCode = @DiscountCode AND InventoryID = @InventoryID";

                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@DiscountCode", discountCode);
                    cmd.Parameters.AddWithValue("@InventoryID", inventoryId);

                    int count = (int)cmd.ExecuteScalar();
                    return count > 0;
                }
            }
        }


        public static int? GetInventoryIDForDiscount(string discountCode)
        {
            using (SQLiteConnection conn = new SQLiteConnection(CONNECT_STRING))
            {
                string query = "SELECT InventoryID FROM Discounts WHERE DiscountCode = @DiscountCode";
                SQLiteCommand cmd = new SQLiteCommand(query, conn);
                cmd.Parameters.AddWithValue("@DiscountCode", discountCode);

                conn.Open();
                object result = cmd.ExecuteScalar();
                if (result == DBNull.Value || result == null)
                    return null; // No InventoryID linked (cart-level discount)
                return Convert.ToInt32(result);
            }
        }




        public static void CloseDatabase()
        {
            if (_cntDatabase == null) return;

            try
            {
                if (_cntDatabase.State != ConnectionState.Closed)
                    _cntDatabase.Close();
            }
            catch
            {
                // ignore close errors
            }
            finally
            {
                _cntDatabase.Dispose();
                _cntDatabase = null;
            }
        }


        public static DataTable GetAllCustomers()
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();
                string sql = @"
                SELECT 
                    p.PersonID,
                    l.LogonName,
                    (p.NameFirst || ' ' || IFNULL(p.NameMiddle || ' ', '') || p.NameLast) AS FullName,
                    p.Email,
                    p.PhonePrimary
                FROM Person p
                INNER JOIN Logon l ON l.PersonID = p.PersonID
                WHERE l.PositionTitle = 'Customer'
                ORDER BY l.LogonName, p.NameLast, p.NameFirst;";
                ;

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                using (var da = new SQLiteDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading customers:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
            return dt;
        }


        public static DataTable SearchCustomers(string term)
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();
                string sql = @"
                SELECT 
                    p.PersonID,
                    l.LogonName,
                    (p.NameFirst || ' ' || IFNULL(p.NameMiddle || ' ', '') || p.NameLast) AS FullName,
                    p.Email,
                    p.PhonePrimary
                FROM Person p
                INNER JOIN Logon l ON l.PersonID = p.PersonID
                WHERE l.PositionTitle = 'Customer'
                  AND (@t = '' 
                   OR p.NameFirst LIKE '%' || @t || '%'
                   OR p.NameLast  LIKE '%' || @t || '%'
                   OR (p.NameFirst || ' ' || IFNULL(p.NameMiddle || ' ', '') || p.NameLast) LIKE '%' || @t || '%'
                   OR p.Email LIKE '%' || @t || '%'
                   OR p.PhonePrimary LIKE '%' || @t || '%'
                   OR l.LogonName LIKE '%' || @t || '%')
                ORDER BY l.LogonName, p.NameLast, p.NameFirst;";


                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@t", term ?? string.Empty);
                    using (var da = new SQLiteDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error searching customers:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
            return dt;
        }


        public static DataTable GetAllInventoryProducts()
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();

                string sql = @"
                SELECT 
                    InventoryID,
                    ItemName,
                    ItemDescription,
                    ItemImage,          -- varbinary(MAX) nullable
                    RetailPrice,
                    Quantity
                FROM Inventory
                WHERE IFNULL(Discontinued, 0) = 0       -- do not show discontinued in product lists
                ORDER BY ItemName;";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                using (var da = new SQLiteDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading inventory:\n" + ex.Message, "Database Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
            return dt;
        }


        public static DataTable GetPeopleForEdit(string term)
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();
                string sql = @"
                    SELECT 
                        p.PersonID,
                        (p.NameFirst || ' ' || p.NameLast) AS FullName,
                        p.Title, p.NameFirst, p.NameMiddle, p.NameLast, p.Suffix,
                        p.Address1, p.Address2, p.Address3, p.City, p.State, p.Zipcode,
                        p.Email, p.PhonePrimary, p.PhoneSecondary,
                        l.LogonName,
                        l.PositionTitle
                    FROM Person p
                    LEFT JOIN Logon l ON l.PersonID = p.PersonID
                    WHERE (@t = '' OR 
                           p.NameFirst LIKE '%' || @t || '%' OR
                           p.NameLast  LIKE '%' || @t || '%' OR
                           (p.NameFirst || ' ' || p.NameLast) LIKE '%' || @t || '%' OR
                           p.Email LIKE '%' || @t || '%' OR
                           p.PhonePrimary LIKE '%' || @t || '%')
                    ORDER BY p.NameLast, p.NameFirst;"; 

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@t", term ?? string.Empty);
                    using (var da = new SQLiteDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading edit data:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
            return dt;
        }

        public static bool SavePeopleEdits(DataTable changes)
        {
            if (changes == null || changes.Rows.Count == 0) return true;

            try
            {
                OpenDatabase();
                using (var tx = _cntDatabase.BeginTransaction())
                {
                    foreach (DataRow r in changes.Rows)
                    {
                        if (r.RowState != DataRowState.Modified) continue;

                        if (!changes.Columns.Contains("PersonID"))
                            continue;

                        int personId = Convert.ToInt32(r["PersonID"]);

                        string update = @"
                        UPDATE Person
                        SET 
                            Title=@Title, NameFirst=@NameFirst, NameMiddle=@NameMiddle, NameLast=@NameLast, Suffix=@Suffix,
                            Address1=@Address1, Address2=@Address2, Address3=@Address3, City=@City, State=@State, Zipcode=@Zipcode,
                            Email=@Email, PhonePrimary=@PhonePrimary, PhoneSecondary=@PhoneSecondary
                        WHERE PersonID=@PersonID;";

                        using (var cmd = new SQLiteCommand(update, _cntDatabase, tx))
                        {
                            object Get(string c) => changes.Columns.Contains(c) ? (r[c] ?? DBNull.Value) : DBNull.Value;

                            cmd.Parameters.AddWithValue("@PersonID", personId);
                            cmd.Parameters.AddWithValue("@Title", Get("Title") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@NameFirst", Get("NameFirst") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@NameMiddle", Get("NameMiddle") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@NameLast", Get("NameLast") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Suffix", Get("Suffix") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Address1", Get("Address1") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Address2", Get("Address2") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Address3", Get("Address3") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@City", Get("City") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@State", Get("State") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Zipcode", Get("Zipcode") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Email", Get("Email") ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@PhonePrimary", Validation.SaveVerification(Get("PhonePrimary")?.ToString() ?? ""));
                            cmd.Parameters.AddWithValue("@PhoneSecondary", string.IsNullOrWhiteSpace(Get("PhoneSecondary")?.ToString() ?? "")
                                                                          ? (object)DBNull.Value
                                                                          : Validation.SaveVerification(Get("PhoneSecondary")?.ToString() ?? ""));

                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving edits:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                CloseDatabase();
            }
        }

        public static bool IsLogonNameAvailable(string logonName, SQLiteConnection conn, SQLiteTransaction tx)
        {
            const string sql = @"SELECT COUNT(*) FROM Logon WHERE LogonName = @u";
            using (var cmd = new SQLiteCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@u", logonName);
                int count = Convert.ToInt32(cmd.ExecuteScalar());
                return count == 0;
            }
        }

        public static int? GetPositionIdByTitle(string positionTitle, SQLiteConnection conn, SQLiteTransaction tx)
        {
            const string sql = @"SELECT PositionID FROM Position WHERE PositionTitle = @t";
            using (var cmd = new SQLiteCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@t", positionTitle);
                object o = cmd.ExecuteScalar();
                return (o == null || o == DBNull.Value) ? (int?)null : Convert.ToInt32(o);
            }
        }


        public static bool InsertPersonAndLogon(
        string title,
        string first,
        string middle,
        string last,
        string suffix,
        string address1,
        string address2,
        string address3,
        string city,
        string state,
        string zipcode,
        string email,
        string phonePrimary,
        string phoneSecondary,
        string logonName,
        string positionTitle,
        string initialPassword)
        {
            try
            {
                using (var conn = new SQLiteConnection(CONNECT_STRING))
                {
                    conn.Open();

                    using (var tx = conn.BeginTransaction())
                    {
                        // 1) resolve PositionID
                        int? positionId = GetPositionIdByTitle(positionTitle, conn, tx);
                        if (!positionId.HasValue)
                        {
                            MessageBox.Show("Invalid Position Title selected.", "Insert",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            try { tx.Rollback(); } catch { }
                            return false;
                        }

                        // 2) ensure unique LogonName
                        if (!IsLogonNameAvailable(logonName, conn, tx))
                        {
                            MessageBox.Show("That username is already in use. Please choose another.", "Insert",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            try { tx.Rollback(); } catch { }
                            return false;
                        }

                        // 3) insert Person + get new PersonID (SQLite way)
                        const string insertPerson = @"
                        INSERT INTO Person
                        (Title, NameFirst, NameMiddle, NameLast, Suffix, Address1, Address2, Address3, City, State, Zipcode, Email, PhonePrimary, PhoneSecondary, PositionID)
                        VALUES
                        (@Title, @First, @Middle, @Last, @Suffix, @A1, @A2, @A3, @City, @State, @Zip, @Email, @Phone1, @Phone2, @PosID);
                        SELECT last_insert_rowid();";

                        int personId;
                        using (var cmd = new SQLiteCommand(insertPerson, conn, tx))
                        {
                            // Helper: empty string -> DBNull
                            object DbNullIfBlank(string s) =>
                                string.IsNullOrWhiteSpace(s) ? (object)DBNull.Value : s.Trim();

                            cmd.Parameters.AddWithValue("@Title", DbNullIfBlank(title));
                            cmd.Parameters.AddWithValue("@First", first?.Trim() ?? "");
                            cmd.Parameters.AddWithValue("@Middle", DbNullIfBlank(middle));
                            cmd.Parameters.AddWithValue("@Last", last?.Trim() ?? "");
                            cmd.Parameters.AddWithValue("@Suffix", DbNullIfBlank(suffix));
                            cmd.Parameters.AddWithValue("@A1", address1?.Trim() ?? "");
                            cmd.Parameters.AddWithValue("@A2", DbNullIfBlank(address2));
                            cmd.Parameters.AddWithValue("@A3", DbNullIfBlank(address3));
                            cmd.Parameters.AddWithValue("@City", city?.Trim() ?? "");
                            cmd.Parameters.AddWithValue("@State", state?.Trim() ?? "");
                            cmd.Parameters.AddWithValue("@Zip", zipcode?.Trim() ?? "");
                            cmd.Parameters.AddWithValue("@Email", DbNullIfBlank(email));
                            cmd.Parameters.AddWithValue("@Phone1", phonePrimary?.Trim() ?? "");
                            cmd.Parameters.AddWithValue("@Phone2", DbNullIfBlank(phoneSecondary));
                            cmd.Parameters.AddWithValue("@PosID", positionId.Value);

                            // last_insert_rowid() returns Int64
                            personId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        // 4) insert Logon
                        const string insertLogon = @"
                        INSERT INTO Logon
                        (PersonID, LogonName, Password, FirstChallengeQuestion, FirstChallengeAnswer,
                         SecondChallengeQuestion, SecondChallengeAnswer, ThirdChallengeQuestion, ThirdChallengeAnswer, PositionTitle)
                        VALUES
                        (@PID, @User, @Pwd, NULL, NULL, NULL, NULL, NULL, NULL, @PosTitle);";

                        using (var cmd = new SQLiteCommand(insertLogon, conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@PID", personId);
                            cmd.Parameters.AddWithValue("@User", logonName?.Trim() ?? "");
                            cmd.Parameters.AddWithValue("@Pwd", initialPassword ?? "");
                            cmd.Parameters.AddWithValue("@PosTitle", positionTitle?.Trim() ?? "");
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error creating account:\n" + ex.Message, "Insert Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }



        public static DataTable GetInventoryForEdit(string term = "")
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();
                string sql = @"
                SELECT 
                    InventoryID,
                    ItemName,
                    ItemDescription,
                    CategoryID,
                    RetailPrice,
                    Cost,
                    Quantity,
                    RestockThreshold,
                    Discontinued
                FROM Inventory
                WHERE (@t = '' OR ItemName LIKE '%' + @t + '%' OR ItemDescription LIKE '%' + @t + '%')
                ORDER BY ItemName;";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@t", term ?? string.Empty);
                    using (var da = new SQLiteDataAdapter(cmd))
                        da.Fill(dt);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading inventory:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { CloseDatabase(); }
            return dt;
        }

        public static bool SaveInventoryEdits(DataTable changes)
        {
            if (changes == null || changes.Rows.Count == 0) return true;

            try
            {
                OpenDatabase();
                using (var tx = _cntDatabase.BeginTransaction())
                {
                    string insertSql = @"
                    INSERT INTO Inventory
                        (ItemName, ItemDescription, CategoryID, RetailPrice, Cost, Quantity, RestockThreshold, Discontinued)
                    VALUES
                        (@ItemName, @ItemDescription, @CategoryID, @RetailPrice, @Cost, @Quantity, @RestockThreshold, @Discontinued);
                    SELECT SCOPE_IDENTITY();";

                    string updateSql = @"
                    UPDATE Inventory
                    SET
                        ItemName = @ItemName,
                        ItemDescription = @ItemDescription,
                        CategoryID = @CategoryID,
                        RetailPrice = @RetailPrice,
                        Cost = @Cost,
                        Quantity = @Quantity,
                        RestockThreshold = @RestockThreshold,
                        Discontinued = @Discontinued
                    WHERE InventoryID = @InventoryID;";

                    foreach (DataRow r in changes.Rows)
                    {
                        if (r.RowState == DataRowState.Added)
                        {
                            using (var cmd = new SQLiteCommand(insertSql, _cntDatabase, tx))
                            {
                                cmd.Parameters.AddWithValue("@ItemName", r["ItemName"] ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@ItemDescription", r["ItemDescription"] ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@CategoryID", SafeInt(r["CategoryID"]));
                                cmd.Parameters.AddWithValue("@RetailPrice", SafeDecimal(r["RetailPrice"]));
                                cmd.Parameters.AddWithValue("@Cost", SafeDecimal(r["Cost"]));
                                cmd.Parameters.AddWithValue("@Quantity", SafeInt(r["Quantity"]));
                                cmd.Parameters.AddWithValue("@RestockThreshold", SafeInt(r["RestockThreshold"]));
                                cmd.Parameters.AddWithValue("@Discontinued", SafeBool(r["Discontinued"]));
                                var id = cmd.ExecuteScalar();
                                if (id != null && id != DBNull.Value)
                                    r["InventoryID"] = Convert.ToInt32(id);
                            }
                        }
                        else if (r.RowState == DataRowState.Modified)
                        {
                            using (var cmd = new SQLiteCommand(updateSql, _cntDatabase, tx))
                            {
                                cmd.Parameters.AddWithValue("@InventoryID", SafeInt(r["InventoryID"]));
                                cmd.Parameters.AddWithValue("@ItemName", r["ItemName"] ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@ItemDescription", r["ItemDescription"] ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@CategoryID", SafeInt(r["CategoryID"]));
                                cmd.Parameters.AddWithValue("@RetailPrice", SafeDecimal(r["RetailPrice"]));
                                cmd.Parameters.AddWithValue("@Cost", SafeDecimal(r["Cost"]));
                                cmd.Parameters.AddWithValue("@Quantity", SafeInt(r["Quantity"]));
                                cmd.Parameters.AddWithValue("@RestockThreshold", SafeInt(r["RestockThreshold"]));
                                cmd.Parameters.AddWithValue("@Discontinued", SafeBool(r["Discontinued"]));
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    tx.Commit();
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving inventory changes:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally { CloseDatabase(); }
        }

        private static int SafeInt(object o)
        {
            if (o == null || o == DBNull.Value) return 0;
            int.TryParse(o.ToString(), out int v); return v;
        }
        private static decimal SafeDecimal(object o)
        {
            if (o == null || o == DBNull.Value) return 0m;
            decimal.TryParse(o.ToString(), out decimal d); return d;
        }
        private static bool SafeBool(object o)
        {
            if (o == null || o == DBNull.Value) return false;
            bool.TryParse(o.ToString(), out bool b); return b;
        }

        public static DataTable GetDiscountsForEdit()
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();
                string sql = @"
                SELECT 
                    DiscountID,
                    DiscountCode,
                    [Description],
                    DiscountLevel,
                    InventoryID,
                    DiscountType,
                    DiscountPercentage,
                    DiscountDollarAmount,
                    StartDate,
                    ExpirationDate
                FROM Discounts
                ORDER BY DiscountID;";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                using (var da = new SQLiteDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading discounts:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
            return dt;
        }

        public static bool SaveDiscountEdits(DataTable changes)
        {
            if (changes == null || changes.Rows.Count == 0) return true;

            try
            {
                OpenDatabase();
                using (var tx = _cntDatabase.BeginTransaction())
                {
                    try
                    {
                        foreach (DataRow r in changes.Rows)
                        {
                            if (r.RowState == DataRowState.Added)
                            {
                                const string insert = @"
                                INSERT INTO Discounts
                                (DiscountCode, Description, DiscountLevel, InventoryID, DiscountType,
                                 DiscountPercentage, DiscountDollarAmount, StartDate, ExpirationDate)
                                VALUES
                                (@DiscountCode, @Description, @DiscountLevel, @InventoryID, @DiscountType,
                                 @DiscountPercentage, @DiscountDollarAmount, @StartDate, @ExpirationDate);";

                                using (var cmd = new SQLiteCommand(insert, _cntDatabase, tx))
                                {
                                    AddDiscountParams(cmd, r, includeId: false);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            else if (r.RowState == DataRowState.Modified)
                            {
                                const string update = @"
                                UPDATE Discounts
                                SET DiscountCode = @DiscountCode,
                                    Description = @Description,
                                    DiscountLevel = @DiscountLevel,
                                    InventoryID = @InventoryID,
                                    DiscountType = @DiscountType,
                                    DiscountPercentage = @DiscountPercentage,
                                    DiscountDollarAmount = @DiscountDollarAmount,
                                    StartDate = @StartDate,
                                    ExpirationDate = @ExpirationDate
                                WHERE DiscountID = @DiscountID;";

                                using (var cmd = new SQLiteCommand(update, _cntDatabase, tx))
                                {
                                    AddDiscountParams(cmd, r, includeId: true);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            // ignore Deleted/Unchanged
                        }

                        tx.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        // Always rollback the transaction and let the caller show a friendly message
                        try { tx.Rollback(); } catch { /* ignore */ }

                        // Optional: dev-only breadcrumb (no UI popup from DAL)
                        Debug.WriteLine($"[DB] SaveDiscountEdits failed: {ex}");

                        return false;
                    }
                }
            }
            finally
            {
                CloseDatabase();
            }
        }


        // Helper to add parameters, safely handling nullables
        private static void AddDiscountParams(SQLiteCommand cmd, DataRow r, bool includeId)
        {
            object Get(string col)
                => r.Table.Columns.Contains(col) ? (r[col] == DBNull.Value || string.IsNullOrWhiteSpace(r[col]?.ToString()) ? (object)DBNull.Value : r[col]) : (object)DBNull.Value;

            if (includeId)
                cmd.Parameters.AddWithValue("@DiscountID", r["DiscountID"]);

            cmd.Parameters.AddWithValue("@DiscountCode", (object)(r["DiscountCode"]?.ToString() ?? "").Trim());

            cmd.Parameters.AddWithValue("@Description", Get("Description"));

            // ints (nullable)
            cmd.Parameters.AddWithValue("@DiscountLevel", Get("DiscountLevel"));
            cmd.Parameters.AddWithValue("@InventoryID", Get("InventoryID"));
            cmd.Parameters.AddWithValue("@DiscountType", Get("DiscountType"));

            // decimals (nullable)
            cmd.Parameters.AddWithValue("@DiscountPercentage", Get("DiscountPercentage"));
            cmd.Parameters.AddWithValue("@DiscountDollarAmount", Get("DiscountDollarAmount"));

            // dates (nullable)
            cmd.Parameters.AddWithValue("@StartDate", Get("StartDate"));
            cmd.Parameters.AddWithValue("@ExpirationDate", Get("ExpirationDate"));
        }

        public static DataTable GetAllOrders()
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();

                // Orders + customer/employee logon names if available
                string sql = @"
                SELECT 
                    o.OrderID,
                    o.OrderDate,
                    c.LogonName  AS CustomerLogon,
                    e.LogonName  AS EmployeeLogon,
                    o.PersonID   AS CustomerPersonID,
                    o.EmployeeID AS EmployeePersonID,
                    o.DiscountID
                FROM Orders o
                LEFT JOIN Logon c ON c.PersonID = o.PersonID
                LEFT JOIN Logon e ON e.PersonID = o.EmployeeID
                ORDER BY o.OrderDate DESC, o.OrderID DESC;";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                using (var da = new SQLiteDataAdapter(cmd))
                {
                    da.Fill(dt);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading orders:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
            return dt;
        }

        public static DataTable GetOrderDetails(int orderId)
        {
            var dt = new DataTable();
            try
            {
                OpenDatabase();


                string sql = @"
                WITH Lines AS (
                    SELECT 
                        od.OrderID,
                        od.InventoryID,
                        i.ItemName,
                        i.Cost,
                        od.Quantity,
                        CAST(i.Cost * od.Quantity AS decimal(18,2)) AS LineTotal,
                        SUM(CAST(i.Cost * od.Quantity AS decimal(18,2))) OVER (PARTITION BY od.OrderID) AS OrderSubtotal
                    FROM OrderDetails od
                    INNER JOIN Inventory i ON i.InventoryID = od.InventoryID
                    WHERE od.OrderID = @OrderID
                ),
                Disc AS (
                    SELECT 
                        o.OrderID,
                        d.DiscountID,
                        d.DiscountLevel,
                        d.InventoryID AS DiscountInventoryID,
                        d.DiscountType,
                        d.DiscountPercentage,
                        d.DiscountDollarAmount
                    FROM Orders o
                    LEFT JOIN Discounts d ON d.DiscountID = o.DiscountID
                    WHERE o.OrderID = @OrderID
                )
                SELECT 
                    L.OrderID,
                    L.InventoryID,
                    L.ItemName,
                    L.Cost,
                    L.Quantity,
                    L.LineTotal,
                    D.DiscountID,
                    D.DiscountLevel,
                    D.DiscountInventoryID,
                    D.DiscountType,
                    D.DiscountPercentage,
                    D.DiscountDollarAmount,
                    -- Per-line discount
                    CAST(
                        CASE 
                            WHEN D.DiscountID IS NULL THEN 0
                            -- Item-level: only when this line's InventoryID matches
                            WHEN D.DiscountLevel = 1 AND D.DiscountInventoryID IS NOT NULL AND D.DiscountInventoryID = L.InventoryID THEN
                                CASE 
                                    WHEN D.DiscountType = 0 AND D.DiscountPercentage IS NOT NULL
                                        THEN L.LineTotal * (D.DiscountPercentage / 100.0)
                                    WHEN D.DiscountType = 1 AND D.DiscountDollarAmount IS NOT NULL
                                        THEN D.DiscountDollarAmount * L.Quantity
                                    ELSE 0
                                END
                            -- Cart-level:
                            WHEN D.DiscountLevel = 0 THEN
                                CASE 
                                    -- percentage on each line
                                    WHEN D.DiscountType = 0 AND D.DiscountPercentage IS NOT NULL
                                        THEN L.LineTotal * (D.DiscountPercentage / 100.0)
                                    -- dollar amount pro-rated by line share of subtotal
                                    WHEN D.DiscountType = 1 AND D.DiscountDollarAmount IS NOT NULL
                                        THEN CASE 
                                                WHEN L.OrderSubtotal > 0 
                                                    THEN (L.LineTotal / NULLIF(L.OrderSubtotal,0)) * D.DiscountDollarAmount
                                                ELSE 0
                                             END
                                    ELSE 0
                                END
                            ELSE 0
                        END
                    AS decimal(18,2)) AS LineDiscount,
                    -- Line after discount (never below 0)
                    CAST(
                        CASE 
                            WHEN 
                                (L.LineTotal -
                                    CASE 
                                        WHEN D.DiscountID IS NULL THEN 0
                                        WHEN D.DiscountLevel = 1 AND D.DiscountInventoryID IS NOT NULL AND D.DiscountInventoryID = L.InventoryID THEN
                                            CASE 
                                                WHEN D.DiscountType = 0 AND D.DiscountPercentage IS NOT NULL
                                                    THEN L.LineTotal * (D.DiscountPercentage / 100.0)
                                                WHEN D.DiscountType = 1 AND D.DiscountDollarAmount IS NOT NULL
                                                    THEN D.DiscountDollarAmount * L.Quantity
                                                ELSE 0
                                            END
                                        WHEN D.DiscountLevel = 0 THEN
                                            CASE 
                                                WHEN D.DiscountType = 0 AND D.DiscountPercentage IS NOT NULL
                                                    THEN L.LineTotal * (D.DiscountPercentage / 100.0)
                                                WHEN D.DiscountType = 1 AND D.DiscountDollarAmount IS NOT NULL
                                                    THEN CASE 
                                                            WHEN L.OrderSubtotal > 0 
                                                                THEN (L.LineTotal / NULLIF(L.OrderSubtotal,0)) * D.DiscountDollarAmount
                                                            ELSE 0
                                                         END
                                                ELSE 0
                                            END
                                        ELSE 0
                                    END
                                ) < 0 
                            THEN 0 
                            ELSE 
                                (L.LineTotal -
                                    CASE 
                                        WHEN D.DiscountID IS NULL THEN 0
                                        WHEN D.DiscountLevel = 1 AND D.DiscountInventoryID IS NOT NULL AND D.DiscountInventoryID = L.InventoryID THEN
                                            CASE 
                                                WHEN D.DiscountType = 0 AND D.DiscountPercentage IS NOT NULL
                                                    THEN L.LineTotal * (D.DiscountPercentage / 100.0)
                                                WHEN D.DiscountType = 1 AND D.DiscountDollarAmount IS NOT NULL
                                                    THEN D.DiscountDollarAmount * L.Quantity
                                                ELSE 0
                                            END
                                        WHEN D.DiscountLevel = 0 THEN
                                            CASE 
                                                WHEN D.DiscountType = 0 AND D.DiscountPercentage IS NOT NULL
                                                    THEN L.LineTotal * (D.DiscountPercentage / 100.0)
                                                WHEN D.DiscountType = 1 AND D.DiscountDollarAmount IS NOT NULL
                                                    THEN CASE 
                                                            WHEN L.OrderSubtotal > 0 
                                                                THEN (L.LineTotal / NULLIF(L.OrderSubtotal,0)) * D.DiscountDollarAmount
                                                            ELSE 0
                                                         END
                                                ELSE 0
                                            END
                                        ELSE 0
                                    END
                                )
                        END
                    AS decimal(18,2)) AS LineTotalAfterDiscount
                FROM Lines L
                LEFT JOIN Disc D ON D.OrderID = L.OrderID
                ORDER BY L.InventoryID;";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@OrderID", orderId);
                    using (var da = new SQLiteDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading order details:\n" + ex.Message, "Database Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }
            return dt;
        }


        public static DataTable GetProfitsReport(DateTime startDate, DateTime endDate)
        {
            // Uses the app’s tax 8.25%. Adjust if you have a Tax column.
            const decimal TAX = 0.0825m;
            var dt = new DataTable();
            OpenDatabase();


            string sql = @"
            ;WITH LineBase AS
            (
              SELECT 
                o.OrderID,
                o.OrderDate,
                o.PersonID,
                o.EmployeeID,
                o.DiscountID,
                od.InventoryID,
                od.Quantity,
                i.RetailPrice,
                (i.RetailPrice * od.Quantity) AS LineSubtotal
              FROM Orders o
              JOIN OrderDetails od ON o.OrderID = od.OrderID
              JOIN Inventory i   ON od.InventoryID = i.InventoryID
              WHERE o.OrderDate >= @Start AND o.OrderDate <= @End
            ),
            OrderSub AS
            (
              SELECT OrderID, SUM(LineSubtotal) AS OrderSubtotal
              FROM LineBase
              GROUP BY OrderID
            ),
            OrderItemDiscount AS
            (
              -- item-level discount: only lines where discount.InventoryID = line.InventoryID
              SELECT 
                lb.OrderID,
                SUM(
                  CASE 
                    WHEN d.DiscountLevel = 1 AND d.DiscountType = 0 AND d.DiscountPercentage IS NOT NULL AND d.InventoryID = lb.InventoryID
                      THEN lb.LineSubtotal * (d.DiscountPercentage / 100.0)
                    WHEN d.DiscountLevel = 1 AND d.DiscountType = 1 AND d.DiscountDollarAmount IS NOT NULL AND d.InventoryID = lb.InventoryID
                      THEN d.DiscountDollarAmount * lb.Quantity
                    ELSE 0
                  END
                ) AS ItemLevelDiscount
              FROM LineBase lb
              LEFT JOIN Discounts d ON lb.DiscountID = d.DiscountID
              GROUP BY lb.OrderID
            ),
            OrderCartDiscount AS
            (
              -- cart-level discount: apply to subtotal
              SELECT 
                os.OrderID,
                CASE 
                  WHEN d.DiscountLevel = 0 AND d.DiscountType = 0 AND d.DiscountPercentage IS NOT NULL
                    THEN os.OrderSubtotal * (d.DiscountPercentage / 100.0)
                  WHEN d.DiscountLevel = 0 AND d.DiscountType = 1 AND d.DiscountDollarAmount IS NOT NULL
                    THEN CASE WHEN d.DiscountDollarAmount > os.OrderSubtotal THEN os.OrderSubtotal ELSE d.DiscountDollarAmount END
                  ELSE 0
                END AS CartLevelDiscount
              FROM OrderSub os
              LEFT JOIN Orders o ON os.OrderID = o.OrderID
              LEFT JOIN Discounts d ON o.DiscountID = d.DiscountID
            ),
            OrderTotals AS
            (
              SELECT 
                os.OrderID,
                os.OrderSubtotal,
                ISNULL(oid.ItemLevelDiscount,0) AS ItemLevelDiscount,
                ISNULL(ocd.CartLevelDiscount,0) AS CartLevelDiscount
              FROM OrderSub os
              LEFT JOIN OrderItemDiscount oid ON os.OrderID = oid.OrderID
              LEFT JOIN OrderCartDiscount ocd ON os.OrderID = ocd.OrderID
            )
            SELECT 
              o.OrderID,
              o.OrderDate,
              cu.LogonName AS CustomerLogon,
              em.LogonName AS EmployeeLogon,
              ot.OrderSubtotal,
              (ot.ItemLevelDiscount + ot.CartLevelDiscount) AS DiscountAmount,
              CAST(ROUND((ot.OrderSubtotal - (ot.ItemLevelDiscount + ot.CartLevelDiscount)), 2) AS DECIMAL(18,2)) AS DiscountedSubtotal,
              CAST(ROUND((ot.OrderSubtotal - (ot.ItemLevelDiscount + ot.CartLevelDiscount)) * @Tax, 2) AS DECIMAL(18,2)) AS Tax,
              CAST(ROUND((ot.OrderSubtotal - (ot.ItemLevelDiscount + ot.CartLevelDiscount)) * (1+@Tax), 2) AS DECIMAL(18,2)) AS OrderTotal
            FROM OrderTotals ot
            JOIN Orders o ON ot.OrderID = o.OrderID
            LEFT JOIN Logon cu ON cu.PersonID = o.PersonID
            LEFT JOIN Logon em ON em.PersonID = o.EmployeeID
            ORDER BY o.OrderDate;

            -- Add one summary row in code (below).
            ";
            using (var cmd = new SQLiteCommand(sql, _cntDatabase))
            {
                cmd.Parameters.AddWithValue("@Start", startDate);
                cmd.Parameters.AddWithValue("@End", endDate);
                cmd.Parameters.AddWithValue("@Tax", TAX);
                using (var da = new SQLiteDataAdapter(cmd)) da.Fill(dt);
            }
            CloseDatabase();

            // Append a summary row (cumulative period total).
            if (dt.Rows.Count > 0 && dt.Columns.Contains("OrderTotal"))
            {
                decimal sum = 0m;
                foreach (DataRow r in dt.Rows)
                    decimal.TryParse(Convert.ToString(r["OrderTotal"]), out sum /*reuse var*/);

                var total = 0m;
                foreach (DataRow r in dt.Rows)
                    total += r.Field<decimal>("OrderTotal");

                var summary = dt.NewRow();
                summary["CustomerLogon"] = "TOTAL (period)";
                summary["OrderTotal"] = total;
                dt.Rows.Add(summary);
            }

            return dt;
        }




        public static DataTable GetCustomerOrdersReport(DateTime start, DateTime end, string customerLogon = null)
        {
            DateTime startDate = start.Date;
            DateTime endDate = end.Date;

            var dt = new DataTable();

            try
            {
                OpenDatabase();

                const string sql = @"
                SELECT 
                    o.OrderID,
                    o.OrderDate,
                    cu.LogonName AS CustomerLogon,
                    em.LogonName AS EmployeeLogon,
                    o.DiscountID,
                    SUM(od.Quantity * i.Cost) AS OrderTotal
                FROM Orders o
                JOIN OrderDetails od ON od.OrderID = o.OrderID
                JOIN Inventory i      ON i.InventoryID = od.InventoryID
                JOIN Logon cu         ON cu.PersonID   = o.PersonID
                LEFT JOIN Logon em    ON em.PersonID   = o.EmployeeID
                WHERE date(o.OrderDate) BETWEEN date(@StartDate) AND date(@EndDate)
                  AND (@Logon IS NULL OR cu.LogonName = @Logon)
                GROUP BY o.OrderID, o.OrderDate, cu.LogonName, em.LogonName, o.DiscountID
                ORDER BY o.OrderDate DESC, o.OrderID DESC;";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                {
                    // Pass DateTime, not strings
                    cmd.Parameters.AddWithValue("@StartDate", startDate);
                    cmd.Parameters.AddWithValue("@EndDate", endDate);

                    var cleaned = string.IsNullOrWhiteSpace(customerLogon) ? null : customerLogon.Trim();
                    cmd.Parameters.AddWithValue("@Logon", (object)cleaned ?? DBNull.Value);

                    using (var da = new SQLiteDataAdapter(cmd))
                        da.Fill(dt);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error building customer orders report:\n" + ex.Message,
                                "Reports", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                CloseDatabase();
            }

            return dt;
        }






        public static DataTable GetInventoryReport_All()
        {
            var dt = new DataTable();
            OpenDatabase();
            string sql = @"
            SELECT 
              i.InventoryID,
              i.ItemName,
              i.ItemDescription,
              c.CategoryName,
              i.Cost,
              i.RetailPrice,
              i.Quantity AS QuantityOnHand,
              i.RestockThreshold,
              i.Discontinued
            FROM Inventory i
            LEFT JOIN Categories c ON i.CategoryID = c.CategoryID
            ORDER BY i.ItemName;";
            using (var da = new SQLiteDataAdapter(sql, _cntDatabase)) da.Fill(dt);
            CloseDatabase();
            return dt;
        }

        public static DataTable GetInventoryReport_ForSale()
        {
            var dt = new DataTable();
            OpenDatabase();
            string sql = @"
            SELECT 
              i.InventoryID, i.ItemName, i.ItemDescription, c.CategoryName,
              i.Cost, i.RetailPrice, i.Quantity AS QuantityOnHand,
              i.RestockThreshold, i.Discontinued
            FROM Inventory i
            LEFT JOIN Categories c ON i.CategoryID = c.CategoryID
            WHERE ISNULL(i.Discontinued,0) = 0
            ORDER BY i.ItemName;";
            using (var da = new SQLiteDataAdapter(sql, _cntDatabase)) da.Fill(dt);
            CloseDatabase();
            return dt;
        }

        public static DataTable GetInventoryReport_NeedsRestock()
        {
            var dt = new DataTable();
            OpenDatabase();
            string sql = @"
            SELECT 
              i.InventoryID, i.ItemName, i.ItemDescription, c.CategoryName,
              i.Cost, i.RetailPrice, i.Quantity AS QuantityOnHand,
              i.RestockThreshold, i.Discontinued
            FROM Inventory i
            LEFT JOIN Categories c ON i.CategoryID = c.CategoryID
            WHERE i.Quantity < i.RestockThreshold
            ORDER BY i.ItemName;";
            using (var da = new SQLiteDataAdapter(sql, _cntDatabase)) da.Fill(dt);
            CloseDatabase();
            return dt;
        }

        public static bool UpdateInventoryImage(int inventoryId, byte[] imageBytes)
        {
            try
            {
                using (var conn = new SQLiteConnection(CONNECT_STRING))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(@"
                UPDATE Inventory
                SET ItemImage = @Img
                WHERE InventoryID = @InventoryID;", conn))
                    {
                        cmd.Parameters.AddWithValue("@InventoryID", inventoryId);
                        cmd.Parameters.AddWithValue("@Img", (object)imageBytes ?? DBNull.Value);

                        int rows = cmd.ExecuteNonQuery();
                        return rows > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error while updating image:\n" + ex.Message,
                    "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public static DataTable GetSalesReportWithOrderTotals(DateTime start, DateTime end)
        {
            // Uses the app’s tax 8.25%. Adjust if you have a Tax column.
            const decimal TAX = 0.0825m;
            var dt = new DataTable();
            OpenDatabase();

            // Compute per-order subtotal (sum of RetailPrice * qty).
            // Then compute discountAmount per order handling both cart-level and item-level discounts.
            // Finally, OrderTotal = (subtotal - discountAmount) + tax.
            string sql = @"
            ;WITH LineBase AS
            (
              SELECT 
                o.OrderID,
                o.OrderDate,
                o.PersonID,
                o.EmployeeID,
                o.DiscountID,
                od.InventoryID,
                od.Quantity,
                i.RetailPrice,
                (i.RetailPrice * od.Quantity) AS LineSubtotal
              FROM Orders o
              JOIN OrderDetails od ON o.OrderID = od.OrderID
              JOIN Inventory i   ON od.InventoryID = i.InventoryID
              WHERE o.OrderDate >= @Start AND o.OrderDate <= @End
            ),
            OrderSub AS
            (
              SELECT OrderID, SUM(LineSubtotal) AS OrderSubtotal
              FROM LineBase
              GROUP BY OrderID
            ),
            OrderItemDiscount AS
            (
              -- item-level discount: only lines where discount.InventoryID = line.InventoryID
              SELECT 
                lb.OrderID,
                SUM(
                  CASE 
                    WHEN d.DiscountLevel = 1 AND d.DiscountType = 0 AND d.DiscountPercentage IS NOT NULL AND d.InventoryID = lb.InventoryID
                      THEN lb.LineSubtotal * (d.DiscountPercentage / 100.0)
                    WHEN d.DiscountLevel = 1 AND d.DiscountType = 1 AND d.DiscountDollarAmount IS NOT NULL AND d.InventoryID = lb.InventoryID
                      THEN d.DiscountDollarAmount * lb.Quantity
                    ELSE 0
                  END
                ) AS ItemLevelDiscount
              FROM LineBase lb
              LEFT JOIN Discounts d ON lb.DiscountID = d.DiscountID
              GROUP BY lb.OrderID
            ),
            OrderCartDiscount AS
            (
              -- cart-level discount: apply to subtotal
              SELECT 
                os.OrderID,
                CASE 
                  WHEN d.DiscountLevel = 0 AND d.DiscountType = 0 AND d.DiscountPercentage IS NOT NULL
                    THEN os.OrderSubtotal * (d.DiscountPercentage / 100.0)
                  WHEN d.DiscountLevel = 0 AND d.DiscountType = 1 AND d.DiscountDollarAmount IS NOT NULL
                    THEN CASE WHEN d.DiscountDollarAmount > os.OrderSubtotal THEN os.OrderSubtotal ELSE d.DiscountDollarAmount END
                  ELSE 0
                END AS CartLevelDiscount
              FROM OrderSub os
              LEFT JOIN Orders o ON os.OrderID = o.OrderID
              LEFT JOIN Discounts d ON o.DiscountID = d.DiscountID
            ),
            OrderTotals AS
            (
              SELECT 
                os.OrderID,
                os.OrderSubtotal,
                ISNULL(oid.ItemLevelDiscount,0) AS ItemLevelDiscount,
                ISNULL(ocd.CartLevelDiscount,0) AS CartLevelDiscount
              FROM OrderSub os
              LEFT JOIN OrderItemDiscount oid ON os.OrderID = oid.OrderID
              LEFT JOIN OrderCartDiscount ocd ON os.OrderID = ocd.OrderID
            )
            SELECT 
              o.OrderID,
              o.OrderDate,
              cu.LogonName AS CustomerLogon,
              em.LogonName AS EmployeeLogon,
              ot.OrderSubtotal,
              (ot.ItemLevelDiscount + ot.CartLevelDiscount) AS DiscountAmount,
              CAST(ROUND((ot.OrderSubtotal - (ot.ItemLevelDiscount + ot.CartLevelDiscount)), 2) AS DECIMAL(18,2)) AS DiscountedSubtotal,
              CAST(ROUND((ot.OrderSubtotal - (ot.ItemLevelDiscount + ot.CartLevelDiscount)) * @Tax, 2) AS DECIMAL(18,2)) AS Tax,
              CAST(ROUND((ot.OrderSubtotal - (ot.ItemLevelDiscount + ot.CartLevelDiscount)) * (1+@Tax), 2) AS DECIMAL(18,2)) AS OrderTotal
            FROM OrderTotals ot
            JOIN Orders o ON ot.OrderID = o.OrderID
            LEFT JOIN Logon cu ON cu.PersonID = o.PersonID
            LEFT JOIN Logon em ON em.PersonID = o.EmployeeID
            ORDER BY o.OrderDate;

            -- Add one summary row in code (below).
            ";
            using (var cmd = new SQLiteCommand(sql, _cntDatabase))
            {
                cmd.Parameters.AddWithValue("@Start", start);
                cmd.Parameters.AddWithValue("@End", end);
                cmd.Parameters.AddWithValue("@Tax", TAX);
                using (var da = new SQLiteDataAdapter(cmd)) da.Fill(dt);
            }
            CloseDatabase();

            // Append a summary row (cumulative period total).
            if (dt.Rows.Count > 0 && dt.Columns.Contains("OrderTotal"))
            {
                decimal sum = 0m;
                foreach (DataRow r in dt.Rows)
                    decimal.TryParse(Convert.ToString(r["OrderTotal"]), out sum /*reuse var*/);

                var total = 0m;
                foreach (DataRow r in dt.Rows)
                    total += r.Field<decimal>("OrderTotal");

                var summary = dt.NewRow();
                summary["CustomerLogon"] = "TOTAL (period)";
                summary["OrderTotal"] = total;
                dt.Rows.Add(summary);
            }

            return dt;
        }

        public static bool SaveAccountEdits(IEnumerable<dynamic> rows)
        {
            if (rows == null) return true;

            try
            {
                OpenDatabase();
                using (var tx = _cntDatabase.BeginTransaction())
                {
                    foreach (var r in rows)
                    {
                        var sets = new List<string>();
                        using (var cmd = new SQLiteCommand())
                        {
                            cmd.Connection = _cntDatabase;
                            cmd.Transaction = tx;


                            if (!(r.PositionTitle is null))
                            {
                                sets.Add("PositionTitle = @pos");
                                cmd.Parameters.AddWithValue("@pos", r.PositionTitle is DBNull ? (object)DBNull.Value : (string)r.PositionTitle);

                            }


                            if (!(r.AccountDisabled is null))
                            {
                                sets.Add("AccountDisabled = @dis");
                                cmd.Parameters.AddWithValue("@dis", r.AccountDisabled is DBNull ? (object)DBNull.Value : Convert.ToInt32((bool)r.AccountDisabled));

                            }

                            // AccountDeleted (bit)
                            if (!(r.AccountDeleted is null))
                            {
                                sets.Add("AccountDeleted = @del");
                                cmd.Parameters.AddWithValue("@del", r.AccountDeleted is DBNull ? (object)DBNull.Value : Convert.ToInt32((bool)r.AccountDeleted));

                            }

                            if (sets.Count == 0) continue; // nothing to update for this row

                            cmd.CommandText =
                                "UPDATE Logon SET " + string.Join(", ", sets) +
                                " WHERE PersonID = @pid;";

                            cmd.Parameters.AddWithValue("@pid", (int)r.PersonID);


                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                    return true;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseDatabase();
            }
        }


        public static bool InventoryIsAvailable(int inventoryId, bool requireInStock = false)
        {
            try
            {
                OpenDatabase();
                string sql = @"
            SELECT COUNT(1)
            FROM Inventory
            WHERE InventoryID = @id
              AND Discontinued = 0
              " + (requireInStock ? "AND Quantity > 0" : "") + ";";

                using (var cmd = new SQLiteCommand(sql, _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@id", inventoryId);
                    var count = Convert.ToInt32(cmd.ExecuteScalar());
                    return count > 0;
                }
            }
            catch
            {
                // If anything goes wrong, treat as unavailable.
                return false;
            }
            finally
            {
                CloseDatabase();
            }
        }

        public static bool CategoryExists(int categoryId)
        {
            try
            {
                OpenDatabase();
                using (var cmd = new SQLiteCommand(
                    "SELECT COUNT(1) FROM Categories WHERE CategoryID = @id", _cntDatabase))
                {
                    cmd.Parameters.AddWithValue("@id", categoryId);
                    return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                }
            }
            catch
            {
                // If anything goes wrong, fail safe and say it doesn't exist.
                return false;
            }
            finally
            {
                CloseDatabase();
            }
        }

    }
}
