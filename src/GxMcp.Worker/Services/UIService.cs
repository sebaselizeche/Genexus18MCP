using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.WebForm;
using Artech.Genexus.Common.Controls;
using Artech.Common.Controls.ToolStrip;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class UIService
    {
        private readonly KbService _kbService;
        private readonly ObjectService _objectService;

        public UIService(KbService kbService, ObjectService objectService)
        {
            _kbService = kbService;
            _objectService = objectService;
        }

        public string GetUIContext(string target)
        {
            try
            {
                var obj = _objectService.FindObject(target);
                if (obj == null) return "{\"error\": \"Object not found\"}";

                var result = new JObject();
                result["name"] = obj.Name;
                result["type"] = obj.TypeDescriptor.Name;

                if (obj is WebPanel wbp)
                {
                    var part = wbp.Parts.Get<WebFormPart>();
                    result["html"] = GenerateEnhancedHTML(obj, part);
                }
                else if (obj is Transaction trn)
                {
                    var part = trn.Parts.Get<WebFormPart>();
                    result["html"] = GenerateEnhancedHTML(obj, part);
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private string GenerateEnhancedHTML(KBObject obj, WebFormPart part)
        {
            if (part == null) return "<div>No Layout</div>";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'><style>");
            
            // Base Styles
            sb.AppendLine("body, html { font-family: 'Segoe UI', Arial, sans-serif; padding: 0; margin: 0; background-color: #f5f5f5; color: #333; font-size: 11px; height: 100%; overflow: hidden; }");
            sb.AppendLine(".main-container { display: flex; flex-direction: column; height: 100vh; }");
            
            // Header
            sb.AppendLine(".header-bar { display: flex; justify-content: space-between; align-items: center; background: #f3f3f3; padding: 4px 15px; border-bottom: 1px solid #ccc; flex-shrink: 0; }");
            sb.AppendLine(".header-title { font-size: 12px; font-weight: bold; color: #444; display: flex; align-items: center; gap: 8px; }");
            sb.AppendLine(".header-title img { width: 16px; height: 16px; }");
            
            // Tabs Bottom
            sb.AppendLine(".footer-tabs { display: flex; background: #f3f3f3; border-top: 1px solid #ccc; flex-shrink: 0; height: 25px; }");
            sb.AppendLine(".tab-button { padding: 4px 15px; cursor: pointer; border-right: 1px solid #ccc; background: #e1e1e1; color: #666; font-size: 11px; display: flex; align-items: center; }");
            sb.AppendLine(".tab-button.active { background: #fff; color: #000; font-weight: bold; border-top: 2px solid #005a9e; margin-top: -1px; }");
            
            // Content Areas
            sb.AppendLine(".view-content { flex-grow: 1; overflow: auto; background: #fff; display: none; }");
            sb.AppendLine(".view-content.active { display: block; }");
            
            // Design View Styles (GeneXus Specific)
            sb.AppendLine(".gx-design-canvas { background-color: #fff; padding: 20px; min-height: 1000px; min-width: 800px; }");
            sb.AppendLine(".Table, .TableContent, .TableData, .Table100Width { border: 1px dotted #e0e0e0; border-collapse: collapse; width: auto; }");
            sb.AppendLine(".Table100Width { width: 100%; }");
            sb.AppendLine("td { border: 1px dotted #e0e0e0; padding: 2px; vertical-align: top; min-width: 10px; min-height: 20px; }");
            
            // Controls
            sb.AppendLine(".GroupTela { border: 1px solid #c0c0c0; padding: 15px 10px 10px 10px; margin: 10px 0; }");
            sb.AppendLine(".GroupTelaTitle { color: #005a9e; font-weight: bold; font-size: 12px; }");
            sb.AppendLine(".TextBlockTitle { color: #005a9e; font-size: 20px; font-weight: normal; margin-bottom: 15px; display: block; }");
            sb.AppendLine(".TextBlock { color: #000; white-space: nowrap; }");
            sb.AppendLine(".Attribute { border: 1px solid #7f9db9; padding: 2px; background: #fff; color: #000; font-family: 'Segoe UI'; font-size: 11px; width: 100%; box-sizing: border-box; }");
            sb.AppendLine(".ReadonlyAttribute { border: none; background: transparent; }");
            sb.AppendLine(".RequiredfundoAtributo { background-color: #fff; }");
            sb.AppendLine(".Button { background: #005a9e; color: #fff; border: 1px solid #004578; padding: 3px 15px; min-width: 80px; font-size: 11px; cursor: pointer; margin: 2px; }");
            sb.AppendLine(".Button:hover { background: #004578; }");
            
            // XML/HTML View
            sb.AppendLine(".xml-view { padding: 0; margin: 0; font-family: 'Consolas', 'Monaco', monospace; font-size: 12px; background: #fff; }");
            sb.AppendLine("pre { margin: 0; padding: 15px; }");
            
            sb.AppendLine("</style></head><body>");
            
            sb.AppendLine("<div class='main-container'>");
            
            // Header bar
            sb.AppendLine("<div class='header-bar'>");
            sb.AppendLine(string.Format("<div class='header-title'><span>{0}</span></div>", obj.Name));
            sb.AppendLine("</div>");

            // Views
            sb.AppendLine("<div id='view-design' class='view-content active'>");
            sb.AppendLine("<div class='gx-design-canvas'>");
            
            try {
                var tree = WebFormHelper.GetWebTagTree(obj, part.Document.DocumentElement);
                if (tree != null && tree.Root != null) {
                    RenderEnhancedNode(tree, sb, obj);
                }
            } catch (Exception ex) {
                sb.AppendLine("<div style='color:red; padding:20px;'>Render Error: " + HttpUtility.HtmlEncode(ex.Message) + "</div>");
            }
            sb.AppendLine("</div></div>");

            sb.AppendLine("<div id='view-html' class='view-content xml-view'>");
            sb.AppendLine("<pre><code>" + HttpUtility.HtmlEncode(part.Document.OuterXml) + "</code></pre>");
            sb.AppendLine("</div>");

            // Footer Tabs
            sb.AppendLine("<div class='footer-tabs'>");
            sb.AppendLine("<div class='tab-button active' onclick=\"switchTab('design')\">Design</div>");
            sb.AppendLine("<div class='tab-button' onclick=\"switchTab('html')\">HTML</div>");
            sb.AppendLine("</div>");

            sb.AppendLine("</div>"); // end main-container

            // Script for tabs
            sb.AppendLine("<script>");
            sb.AppendLine("function switchTab(tab) {");
            sb.AppendLine("  document.querySelectorAll('.view-content').forEach(v => v.classList.remove('active'));");
            sb.AppendLine("  document.querySelectorAll('.tab-button').forEach(t => t.classList.remove('active'));");
            sb.AppendLine("  if (tab === 'design') {");
            sb.AppendLine("    document.getElementById('view-design').classList.add('active');");
            sb.AppendLine("    document.querySelector('.tab-button:nth-child(1)').classList.add('active');");
            sb.AppendLine("  } else {");
            sb.AppendLine("    document.getElementById('view-html').classList.add('active');");
            sb.AppendLine("    document.querySelector('.tab-button:nth-child(2)').classList.add('active');");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("</script>");

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private void RenderEnhancedNode(Tree<IWebTag> node, System.Text.StringBuilder sb, KBObject kbObj)
        {
            IWebTag tag = node.Root;
            if (tag == null) return;

            string caption = tag.ValStr("Caption");
            string cls = tag.ValStr("Class");
            string controlName = tag.ValStr("ControlName");
            string id = tag.ValStr("id");
            string classref = tag.ValStr("classref");
            string captionExpr = tag.ValStr("CaptionExpression");

            // Extract essential properties for gxprop
            var props = new List<string>();
            string[] commonPropNames = { "ControlName", "Caption", "Class", "Type", "Attribute", "Variable", "InternalName" };
            foreach (string propName in commonPropNames)
            {
                string val = tag.ValStr(propName);
                if (!string.IsNullOrEmpty(val))
                    props.Add($"{propName}={HttpUtility.HtmlAttributeEncode(val)}");
            }
            string gxProp = string.Join(";", props);
            string commonAttrs = string.Format(" classref='{0}' gxcontrol='{1}' gxprop='{2}'", 
                HttpUtility.HtmlAttributeEncode(classref), 
                HttpUtility.HtmlAttributeEncode(tag.Type.ToString()), 
                gxProp);

            if (!string.IsNullOrEmpty(id)) commonAttrs += $" id='{HttpUtility.HtmlAttributeEncode(id)}'";
            if (!string.IsNullOrEmpty(captionExpr)) commonAttrs += $" captionexpression='{HttpUtility.HtmlAttributeEncode(captionExpr)}'";

            switch (tag.Type)
            {
                case WebTagType.Table:
                    string tableClass = string.IsNullOrEmpty(cls) ? "Table" : cls;
                    sb.AppendLine(string.Format("<table class='{0}' cellspacing='1' cellpadding='1'{1}><tbody>", tableClass, commonAttrs));
                    foreach (var child in node.Children) RenderEnhancedNode(child, sb, kbObj);
                    sb.AppendLine("</tbody></table>");
                    break;

                case WebTagType.TableRow:
                    sb.AppendLine("<tr" + commonAttrs + ">");
                    foreach (var child in node.Children) RenderEnhancedNode(child, sb, kbObj);
                    sb.AppendLine("</tr>");
                    break;

                case WebTagType.TableCell:
                    int colSpan = tag.ValNum("Colspan");
                    int rowSpan = tag.ValNum("Rowspan");
                    string tdProps = commonAttrs;
                    if (colSpan > 1) tdProps += string.Format(" colspan='{0}'", colSpan);
                    if (rowSpan > 1) tdProps += string.Format(" rowspan='{0}'", rowSpan);
                    
                    sb.AppendLine(string.Format("<td class='{0}'{1}>", string.IsNullOrEmpty(cls) ? "" : cls, tdProps));
                    foreach (var child in node.Children) RenderEnhancedNode(child, sb, kbObj);
                    sb.AppendLine("</td>");
                    break;

                case WebTagType.Group:
                    sb.AppendLine(string.Format("<fieldset class='{0}'{1}>", string.IsNullOrEmpty(cls) ? "GroupTela" : cls, commonAttrs));
                    if (!string.IsNullOrEmpty(caption))
                        sb.AppendLine(string.Format("<legend class='GroupTelaTitle' contenteditable='false'>{0}</legend>", HttpUtility.HtmlEncode(caption)));
                    sb.AppendLine("<table class='Table100Width'><tbody>");
                    foreach (var child in node.Children) RenderEnhancedNode(child, sb, kbObj);
                    sb.AppendLine("</tbody></table></fieldset>");
                    break;

                case WebTagType.TextBlock:
                    string textCls = string.IsNullOrEmpty(cls) ? "TextBlock" : cls;
                    sb.AppendLine(string.Format("<span class='{0}' contenteditable='false'{1}>{2}</span>", textCls, commonAttrs, HttpUtility.HtmlEncode(caption)));
                    break;

                case WebTagType.Button:
                    string btnCls = string.IsNullOrEmpty(cls) ? "Button" : cls;
                    sb.AppendLine(string.Format("<input type='button' class='{0}' value=' {1} ' contenteditable='false'{2} />", btnCls, HttpUtility.HtmlAttributeEncode(caption), commonAttrs));
                    break;

                case WebTagType.Attribute:
                    string attName = tag.ValStr("Attribute");
                    if (string.IsNullOrEmpty(attName)) attName = tag.ValStr("Variable");
                    string attCls = string.IsNullOrEmpty(cls) ? "Attribute" : cls;
                    sb.AppendLine(string.Format("<input type='text' class='{0}' value='{1}' contenteditable='false'{2} />", attCls, HttpUtility.HtmlAttributeEncode(attName), commonAttrs));
                    break;

                case WebTagType.ErrorViewer:
                    sb.AppendLine(string.Format("<span contenteditable='false'{0}>", commonAttrs));
                    sb.AppendLine("<ul class='ErrorViewer' style='BACKGROUND: none transparent scroll repeat 0% 0%'><li>Errorviewer: ErrorViewer</li></ul></span>");
                    break;

                default:
                    if (tag.IsText) {
                        sb.Append(HttpUtility.HtmlEncode(tag.NodeValue));
                    } else {
                        foreach (var child in node.Children) RenderEnhancedNode(child, sb, kbObj);
                    }
                    break;
            }
        }
    }
}
