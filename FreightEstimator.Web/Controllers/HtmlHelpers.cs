using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace AuthenticationServer.Controllers
{
    /// <summary>
    /// The Plant page's number box: a type=number input that shows an empty box instead of 0 until the user types.
    /// Same markup as the .NET Framework helper it replaces.
    /// </summary>
    public static class HtmlHelpers
    {
        public static IHtmlContent WiseTextBoxFor<TModel>(this IHtmlHelper<TModel> htmlHelper, Expression<Func<TModel, int>> expression, string cssClass = null)
            => NumberBox(htmlHelper, expression, cssClass);

        public static IHtmlContent WiseTextBoxFor<TModel>(this IHtmlHelper<TModel> htmlHelper, Expression<Func<TModel, float>> expression, string cssClass = null)
            => NumberBox(htmlHelper, expression, cssClass);

        private static IHtmlContent NumberBox<TModel, TValue>(IHtmlHelper<TModel> htmlHelper, Expression<Func<TModel, TValue>> expression, string cssClass)
        {
            var provider = htmlHelper.ViewContext.HttpContext.RequestServices.GetRequiredService<IModelExpressionProvider>();
            ModelExpression model = provider.CreateModelExpression(htmlHelper.ViewData, expression);
            string fullName = htmlHelper.ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(model.Name);
            string value = model.Model.ToString();

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

            return htmlHelper.TextBox(fullName, value, null, attributes);
        }
    }
}
