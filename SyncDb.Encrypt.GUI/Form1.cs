using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DbSync.Encrypt;
using SyncDb.Encrypt;

namespace SyncDb.Encrypt.GUI
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void btnEn_Click(object sender, EventArgs e)
        {
            var result = UdEncrypt.Encrypt(txtString.Text, txtKey.Text);
            txtResult.Text = result;
        }

        private void btnDe_Click(object sender, EventArgs e)
        {
            var result = UdEncrypt.Decrypt(txtString.Text, txtKey.Text);
            txtResult.Text = result;
        }
    }
}
