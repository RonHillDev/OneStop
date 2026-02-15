using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data.SQLite;

namespace OneStop
{
    public partial class frmMain : Form
    {
        public frmMain()
        {
            InitializeComponent();

                      
        }
                    

        private void btnLogin_Click(object sender, EventArgs e)
        {
            string username = tbxUsername.Text.Trim();
            string password = tbxPasswod.Text; 

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both username and password.", "Missing Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string positionTitle;
            bool isValidUser = DBConnect.LoginUser(username, password, out positionTitle);

            if (isValidUser)
            {
                if (positionTitle == "Customer")
                {
                    Validation.LoggedInCustomerUsername = username; 
                    CustomerView customerView = new CustomerView();
                    customerView.LoggedInUsername = username;
                    customerView.Show();
                }
                else if (positionTitle == "Manager" || positionTitle == "Employee")
                {
                    Validation.LoggedInManagerUsername = username;
                    ManagerView managerView = new ManagerView();
                    managerView.Show();
                }
                MessageBox.Show($"Login successful. Welcome {positionTitle}.", "Access Granted", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Invalid username or password.", "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnPassword_Click(object sender, EventArgs e)
        {
            frmUserValidate frmUserValidate = new frmUserValidate();
            frmUserValidate.ShowDialog();
        }

        private void btnCreate_Click(object sender, EventArgs e)
        {
            frmCreateAccount frmCreateAccount = new frmCreateAccount();
            frmCreateAccount.ShowDialog();
            
        }

        private void helpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                
                string helpUrl = "https://docs.google.com/document/d/1m8g2o7FHmIOwWJnhM4yK6WJcFcGO9EvNdXOIZajC6po/edit?usp=sharing";

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = helpUrl,
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to open help file:\n" + ex.Message, "Help Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void customerViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CustomerView customerView = new CustomerView(); 
            customerView.Show();
        }

        private void adminViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ManagerView managerView = new ManagerView();
            managerView.Show();
        }
    }
}
