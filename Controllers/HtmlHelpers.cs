using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Html;

namespace AuthenticationServer.Controllers
{
    public static class HtmlHelpers
    {
        public static MvcHtmlString WiseTextBoxFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, int>> expression, string cssClass = null)
        { 
            var metadata = ModelMetadata.FromLambdaExpression(expression, htmlHelper.ViewData);
            var name = ExpressionHelper.GetExpressionText(expression);
            var fullName = htmlHelper.ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(name);
            var value = metadata.Model.ToString();

            var attributes = new Dictionary<string, object>
            {
                { "type", "number" },
                { "value", value == "0" ? "" : value },
                { "onfocus", "if (this.value == '0') { this.value = ''; }" },
                { "onblur", "if (this.value == '') { this.value = '0'; }" }
            };

            if (!string.IsNullOrEmpty(cssClass))
            {
                attributes.Add("class", cssClass);
            }

            return htmlHelper.TextBox(fullName, value, attributes);
        }

        public static MvcHtmlString WiseTextBoxFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, float>> expression, string cssClass = null)
        {
            var metadata = ModelMetadata.FromLambdaExpression(expression, htmlHelper.ViewData);
            var name = ExpressionHelper.GetExpressionText(expression);
            var fullName = htmlHelper.ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(name);
            var value = metadata.Model.ToString();

            var attributes = new Dictionary<string, object>
            {
                { "type", "number" },
                { "value", value == "0" ? "" : value },
                { "onfocus", "if (this.value == '0') { this.value = ''; }" },
                { "onblur", "if (this.value == '') { this.value = '0'; }" }
            };

            if (!string.IsNullOrEmpty(cssClass))
            {
                attributes.Add("class", cssClass);
            }

            return htmlHelper.TextBox(fullName, value, attributes);
        }
    }
}