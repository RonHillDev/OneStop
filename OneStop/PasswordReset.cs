using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OneStop
{
    public partial class frmPasswordReset : Form
    {
        public frmPasswordReset()
        {
            InitializeComponent();
        }

        private void btnSubmit_Click(object sender, EventArgs e)
        {
            string newPassword = tbxEnterReset.Text.Trim();
            string confirmPassword = tbxConfirmReset.Text.Trim();

            if (newPassword != confirmPassword)
            {
                MessageBox.Show("Passwords do not match.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!Validation.IsValidPassword(newPassword))
            {
                MessageBox.Show("Password must be 8–20 characters and include at least one uppercase letter, one lowercase letter, one digit, one special character ()!@#$%^&*, and no spaces.",
                                "Invalid Password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string logonName = frmUserValidate.SessionInfo.CurrentResetUser;

            bool success = DBConnect.ResetPassword(logonName, newPassword);

            if (success)
            {
                MessageBox.Show("Password reset successful.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
            else
            {
                MessageBox.Show("Password reset failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
