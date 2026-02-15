using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OneStop
{
    public partial class frmCreateAccount : Form
    {
        public frmCreateAccount()
        {
            InitializeComponent();

            DBConnect.QuestionsBox(cbxQuestions1, cbxQuestions2, cbxQuestions3);

            cbxTitle.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxSuffix.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxQuestions1.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxQuestions2.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxQuestions3.DropDownStyle = ComboBoxStyle.DropDownList;
            cbxPosition.DropDownStyle = ComboBoxStyle.DropDownList;
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

        

        private void btnBack_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void cbxShow1_CheckedChanged_1(object sender, EventArgs e)
        {
            tbxEnterPass.PasswordChar = cbxShow1.Checked ? '\0' : '*';
            
        }

        private void cbxShow2_CheckedChanged_1(object sender, EventArgs e)
        {
            tbxEnterPass.PasswordChar = cbxShow2.Checked ? '\0' : '*';
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
    }
}

