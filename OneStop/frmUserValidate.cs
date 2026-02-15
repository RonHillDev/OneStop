using System;
using System.Windows.Forms;

namespace OneStop
{
    public partial class frmUserValidate : Form
    {
        public frmUserValidate()
        {
            InitializeComponent();
        }

        public static class SessionInfo
        {
            public static string CurrentResetUser { get; set; }
        }

        private void btnEnter_Click(object sender, EventArgs e)
        {
            string username = tbxResetUser.Text.Trim();

            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("Please enter your username.");
                return;
            }

            var (q1Prompt, q2Prompt, q3Prompt) = DBConnect.GetSecurityPrompts(username);

            if (!string.IsNullOrEmpty(q1Prompt))
            {
                SessionInfo.CurrentResetUser = username;

                lblQuestion1.Text = q1Prompt;
                lblQuestion2.Text = q2Prompt;
                lblQuestion3.Text = q3Prompt;
            }
            else
            {
                MessageBox.Show("No security questions found for this user.");
            }
        }

        private void btnSubmit_Click(object sender, EventArgs e)
        {
            bool valid = DBConnect.ValidateSecurityAnswers(
        tbxResetUser.Text.Trim(),
        tbxAnswer1.Text.Trim(),
        tbxAnswer2.Text.Trim(),
        tbxAnswer3.Text.Trim());

            if (valid)
            {
                frmPasswordReset resetForm = new frmPasswordReset();
                resetForm.ShowDialog();
            }
            else
            {
                MessageBox.Show("Security answers do not match.", "Validation Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }


    }
}
