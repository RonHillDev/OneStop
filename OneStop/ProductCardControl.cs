using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OneStop
{
    internal class ProductCardControl: UserControl
    {

            private int _inventoryId;
            private string _name;
            private decimal _price;
            private int _stock;
            private string _category;

            private Label lblName, lblPrice, lblStock, lblCategory;
            private Button btnAdd;

            public event Action<ItemData> ProductSelected;  // raised when “Add” is clicked

            public ProductCardControl(int inventoryId, string name, decimal price, int stock, string category = null)
            {
                _inventoryId = inventoryId;
                _name = name;
                _price = price;
                _stock = stock;
                _category = category;

                Width = 260;
                Height = 110;
                BorderStyle = BorderStyle.FixedSingle;
                Margin = new Padding(6);

                lblName = new Label { Text = name, AutoSize = true, Location = new Point(8, 8), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
                lblPrice = new Label { Text = $"Price: ${price:F2}", AutoSize = true, Location = new Point(8, 32) };
                lblStock = new Label { Text = $"Stock: {stock}", AutoSize = true, Location = new Point(8, 54) };
                lblCategory = new Label { Text = string.IsNullOrWhiteSpace(category) ? "" : $"Category: {category}", AutoSize = true, Location = new Point(8, 76) };
                btnAdd = new Button { Text = "Add", Width = 60, Height = 26, Location = new Point(190, 74) };

                btnAdd.Click += (s, e) =>
                {
                    ProductSelected?.Invoke(new ItemData
                    {
                        InventoryID = _inventoryId,
                        Name = _name,
                        Price = _price,
                        Stock = _stock,
                        Description = null,
                        ImageBytes = null
                    });
                };

                Controls.Add(lblName);
                Controls.Add(lblPrice);
                Controls.Add(lblStock);
                Controls.Add(lblCategory);
                Controls.Add(btnAdd);
            }
        }
    }

