using System;
using System.Collections.Specialized;
using System.Web.UI;

namespace NuGetGallery
{
    public partial class MultilineText_EditField : System.Web.DynamicData.FieldTemplateUserControl {
        protected void Page_Load(object sender, EventArgs e) {
            TextBox1.MaxLength = Column.MaxLength;
            TextBox1.ToolTip = Column.Description;
    
            SetUpValidator(RequiredFieldValidator1);
            SetUpValidator(RegularExpressionValidator1);
            SetUpValidator(DynamicValidator1);
        }
    
        protected override void ExtractValues(IOrderedDictionary dictionary) {
            dictionary[Column.Name] = ConvertEditedValue(TextBox1.Text);
        }
    
        public override Control DataControl {
            get {
                return TextBox1;
            }
        }
    
    }
}
