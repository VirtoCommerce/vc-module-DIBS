using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using VirtoCommerce.Domain.Order.Model;
using VirtoCommerce.Domain.Payment.Model;

namespace DiBs.Managers
{
    public class DibsPaymentMethod : PaymentMethod
    {
        private const string md5ParameterString = "merchant={0}&orderid={1}&currency={2}&amount={3}";
        private const string md5ResponseString = "transact={0}&amount={1}&currency={2}";
        private const string md5PaymentOperationRequestParameterString = "merchant={0}&orderid={1}&transact={2}&amount={3}";
        private const string md5VoidOperationRequestParameterString = "merchant={0}&orderid={1}&transact={2}";

        #region constants

        private const string redirectUrl = "DiBs.RedirectUrl";
        private const string acceptUrl = "DiBs.AcceptUrl";
        private const string mode = "DiBs.Mode";
        private const string callbackUrl = "DiBs.CallbackUrl";
        private const string md5Key1 = "DiBs.MD5Key1";
        private const string md5Key2 = "DiBs.MD5Key2";
        private const string merchantId = "DiBs.MerchantId";
        private const string formDesign = "DiBs.FormDesign";
        private const string loginApiUser = "DiBs.ApiUser.Login";
        private const string passwordApiUser = "DiBs.ApiUser.Password";

        private const string md5KeyFormDataName = "md5key";
        private const string acceptUrlFormDataName = "accepturl";
        private const string callbackUrlFormDataName = "callbackurl";
        private const string merchantIdFormDataName = "merchant";
        private const string amountFormDataName = "amount";
        private const string orderIdFormDataName = "orderid";
        private const string transactFormDataName = "transact";
        internal const string orderInternalIdFormDataName = "s_orderinternalid";
        private const string currencyFormDataName = "currency";
        private const string testModeFormDataName = "test";
        private const string languageFormDataName = "lang";
        private const string cancelUrlFormDataName = "cancelurl";
        private const string decoratorFormDataName = "decorator";

        #endregion

        public DibsPaymentMethod(string code) : base(code) { }

        #region settings

        public string RedirectUrl => GetSetting(redirectUrl);

        public string AcceptUrl => GetSetting(acceptUrl);

        public string Mode => GetSetting(mode);

        public string CallbackUrl => GetSetting(callbackUrl);

        public string MD5Key1 => GetSetting(md5Key1);

        public string MD5Key2 => GetSetting(md5Key2);

        public string MerchantId => GetSetting(merchantId);

        public string FormDecorator => GetSetting(formDesign);

        public string LoginApiUser => GetSetting(loginApiUser);

        public string PasswordApiUser => GetSetting(passwordApiUser);

        #endregion

        public override PaymentMethodGroupType PaymentMethodGroupType => PaymentMethodGroupType.Alternative;

        public override PaymentMethodType PaymentMethodType => PaymentMethodType.PreparedForm;

        public override CaptureProcessPaymentResult CaptureProcessPayment(CaptureProcessPaymentEvaluationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Payment == null)
                throw new ArgumentNullException(nameof(context.Payment));

            if(context.Order == null)
                throw new ArgumentNullException(nameof(context.Order));

            var retVal = new CaptureProcessPaymentResult();

            if (!context.Payment.IsApproved && (context.Payment.PaymentStatus == PaymentStatus.Authorized || context.Payment.PaymentStatus == PaymentStatus.Cancelled))
            {
                try
                {
                    var param = new NameValueCollection
                    {
                        {merchantIdFormDataName, MerchantId},
                        {amountFormDataName, MoneyToString(context.Payment.Sum)},
                        {transactFormDataName, context.Payment.OuterId},
                        {orderIdFormDataName, context.Order.Number}
                    };

                    var md5Base = string.Format(md5PaymentOperationRequestParameterString, param[merchantIdFormDataName], param[orderIdFormDataName], param[transactFormDataName], param[amountFormDataName]);
                    param.Add(md5KeyFormDataName, CalculateMD5Hash(md5Base));

                    using (WebClient client = new WebClient())
                    {
                        client.Credentials = new NetworkCredential(LoginApiUser, PasswordApiUser);
                        byte[] responsebytes = client.UploadValues("https://payment.architrade.com/cgi-bin/capture.cgi", "POST", param);
                        string responsebody = Encoding.UTF8.GetString(responsebytes);
                        var response = HttpUtility.ParseQueryString(responsebody);
                        if (response["status"] == "ACCEPTED" && response["result"] == "0")
                        {
                            retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Paid;
                            context.Payment.CapturedDate = DateTime.UtcNow;
                            context.Payment.IsApproved = true;
                            retVal.IsSuccess = true;
                        }
                        else
                        {
                            throw new Exception($"Dibs capture payment request failed. Response data: {responsebody}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    retVal.ErrorMessage = ex.Message;
                }
            }

            return retVal;
        }

        public override PostProcessPaymentResult PostProcessPayment(PostProcessPaymentEvaluationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Payment == null)
                throw new ArgumentNullException(nameof(context.Payment));

            if (context.Order == null)
                throw new ArgumentNullException(nameof(context.Order));

            if (context.Payment.PaymentStatus == PaymentStatus.Pending)
            {
                context.Payment.AuthorizedDate = DateTime.UtcNow;
                context.Payment.Status = PaymentStatus.Authorized.ToString();

                return new PostProcessPaymentResult
                {
                    NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Authorized,
                    OuterId = context.Payment.OuterId = context.Parameters[transactFormDataName],
                    OrderId = context.Order.Number,
                    IsSuccess = ValidatePostProcessRequest(context.Parameters).IsSuccess
                };
            }

            if (context.Payment.PaymentStatus == PaymentStatus.Authorized)
            {
                return new PostProcessPaymentResult
                {
                    OrderId = context.Order.Number,
                    IsSuccess = ValidatePostProcessRequest(context.Parameters).IsSuccess
                };
            }

            throw new Exception($"Post process payment failed: payment status is {context.Payment.PaymentStatus}");
        }

        public override ProcessPaymentResult ProcessPayment(ProcessPaymentEvaluationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var retVal = new ProcessPaymentResult();

            if (context.Order != null && context.Store != null && context.Payment != null)
            {
                if (!(!string.IsNullOrEmpty(context.Store.SecureUrl) || !string.IsNullOrEmpty(context.Store.Url)))
                    throw new NullReferenceException("store must specify Url or SecureUrl property");

                var orderId = context.Order.Number;

                //get md5 hash passing the order number, currency ISO code and order total
                var md5Hash = CalculateMD5Hash(orderId, context.Order.Currency, MoneyToString(context.Order.Total));

                var reqparm = new NameValueCollection
                {
                    {acceptUrlFormDataName, AcceptUrl},
                    {callbackUrlFormDataName, CallbackUrl},
                    {cancelUrlFormDataName, AcceptUrl},
                    {merchantIdFormDataName, MerchantId},
                    {orderIdFormDataName, orderId},
                    {orderInternalIdFormDataName, context.Order.Id},
                    {amountFormDataName, MoneyToString(context.Order.Total)},
                    {currencyFormDataName, context.Order.Currency},
                    {languageFormDataName, context.Store.DefaultLanguage.Substring(0, 2)},
                    {md5KeyFormDataName, md5Hash},
                    {decoratorFormDataName, FormDecorator}
                };

                if (Mode == "test")
                {
                    reqparm.Add(testModeFormDataName, "1");
                }

                //build form to post to FlexWin
                var checkoutform = string.Empty;

                checkoutform += $"<form name='dibs' action='{RedirectUrl}' method='POST' charset='UTF-8'>";
                checkoutform += "<p>You'll be redirected to DIBS payment in a moment. If not, click the 'Proceed' button...</p>";

                const string paramTemplateString = "<INPUT TYPE='hidden' name='{0}' value='{1}'>";
                foreach (string key in reqparm)
                    checkoutform += string.Format(paramTemplateString, key, reqparm[key]);
                checkoutform += "<button type='submit'>Proceed</button>";
                checkoutform += "</form>";

                checkoutform += "<script language='javascript'>document.dibs.submit();</script>";

                retVal.HtmlForm = checkoutform;
                retVal.IsSuccess = true;
                retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Pending;
            }
            return retVal;
        }

        public override RefundProcessPaymentResult RefundProcessPayment(RefundProcessPaymentEvaluationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Payment == null)
                throw new ArgumentNullException(nameof(context.Payment));

            if (context.Order == null)
                throw new ArgumentNullException(nameof(context.Order));

            var retVal = new RefundProcessPaymentResult();

            if (context.Payment.IsApproved && context.Payment.PaymentStatus == PaymentStatus.Paid)
            {
                try
                {
                    var param = new NameValueCollection
                    {
                        {merchantIdFormDataName, MerchantId},
                        {transactFormDataName, context.Payment.OuterId},
                        {amountFormDataName, MoneyToString(context.Payment.Sum)},
                        {currencyFormDataName, context.Payment.Currency},
                        {orderIdFormDataName, context.Order.Number},
                        {"textreply", "yes"}
                    };

                    var md5Base = string.Format(md5PaymentOperationRequestParameterString, MerchantId, param[orderIdFormDataName], param[transactFormDataName], param[amountFormDataName]);
                    param.Add(md5KeyFormDataName, CalculateMD5Hash(md5Base));

                    using (WebClient client = new WebClient())
                    {
                        client.Credentials = new NetworkCredential(LoginApiUser, PasswordApiUser);
                        byte[] responsebytes = client.UploadValues("https://payment.architrade.com/cgi-adm/refund.cgi", "POST", param);
                        string responsebody = Encoding.UTF8.GetString(responsebytes);
                        var response = HttpUtility.ParseQueryString(responsebody);
                        if (response["status"] == "ACCEPTED" && response["result"] == "0")
                        {
                            retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Refunded;
                            context.Payment.ModifiedDate = DateTime.UtcNow;
                            context.Payment.IsApproved = false;
                            retVal.IsSuccess = true;
                        }
                        else
                        {
                            throw new Exception($"Dibs refund payment request failed. Response data: {responsebody}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    retVal.ErrorMessage = ex.Message;
                }
            }

            return retVal;
        }

        public override ValidatePostProcessRequestResult ValidatePostProcessRequest(NameValueCollection queryString)
        {
            //calculate hash by transaction id, currency code and amount
            var md5Hash = CalculateResponseMD5Hash(queryString[transactFormDataName], queryString[currencyFormDataName], queryString[amountFormDataName]);

            return new ValidatePostProcessRequestResult
            {
                OuterId = queryString[transactFormDataName],
                IsSuccess = md5Hash.Equals(queryString["authkey"]) //compare calculated hash with the passed in response authkey field
            };
        }

        public override VoidProcessPaymentResult VoidProcessPayment(VoidProcessPaymentEvaluationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Payment == null)
                throw new ArgumentNullException(nameof(context.Payment));

            if (context.Order == null)
                throw new ArgumentNullException(nameof(context.Order));

            var retVal = new VoidProcessPaymentResult();

            if (!context.Payment.IsApproved && context.Payment.PaymentStatus == PaymentStatus.Authorized)
            {
                try
                {
                    var param = new NameValueCollection
                    {
                        {merchantIdFormDataName, MerchantId},
                        {transactFormDataName, context.Payment.OuterId},
                        {orderIdFormDataName, context.Order.Number}
                    };

                    var md5Base = string.Format(md5VoidOperationRequestParameterString, param[merchantIdFormDataName], param[orderIdFormDataName], param[transactFormDataName]);
                    param.Add(md5KeyFormDataName, CalculateMD5Hash(md5Base));

                    using (WebClient client = new WebClient())
                    {
                        client.Credentials = new NetworkCredential(LoginApiUser, PasswordApiUser);
                        byte[] responsebytes = client.UploadValues("https://payment.architrade.com/cgi-adm/cancel.cgi", "POST", param);
                        string responsebody = Encoding.UTF8.GetString(responsebytes);
                        var response = HttpUtility.ParseQueryString(responsebody);
                        if (response["status"] == "ACCEPTED" && response["result"] == "0")
                        {
                            retVal.NewPaymentStatus = context.Payment.PaymentStatus = PaymentStatus.Cancelled;
                            context.Payment.CancelledDate = DateTime.UtcNow;
                            retVal.IsSuccess = true;
                        }
                        else
                        {
                            throw new Exception($"Dibs cancel request failed. Response data: {responsebody}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    retVal.ErrorMessage = ex.Message;
                }
            }
            else if (context.Payment.IsApproved)
            {
                retVal.ErrorMessage = "Payment already approved, use refund";
                retVal.NewPaymentStatus = PaymentStatus.Paid;
            }
            else if (context.Payment.IsCancelled)
            {
                retVal.ErrorMessage = "Payment already canceled";
                retVal.NewPaymentStatus = PaymentStatus.Voided;
            }

            return retVal;
        }

        private string GetMD5Hash(string datastr)
        {
            HashAlgorithm mhash = new MD5CryptoServiceProvider();
            string res = string.Empty;

            byte[] bytValue = Encoding.UTF8.GetBytes(datastr);

            byte[] bytHash = mhash.ComputeHash(bytValue);

            mhash.Clear();

            for (int i = 0; i < bytHash.Length; i++)
            {
                if (bytHash[i] < 16)
                {
                    res += "0" + bytHash[i].ToString("x");
                }
                else
                {
                    res += bytHash[i].ToString("x");
                }
            }

            return res;
        }

        public string CalculateMD5Hash(string value)
        {
            return GetMD5Hash(MD5Key2 + GetMD5Hash(MD5Key1 + value));
        }

        private string CalculateMD5Hash(string orderId, string currency, string amount)
        {
            var md5 = string.Format(md5ParameterString, MerchantId, orderId, currency, amount);
            md5 = GetMD5Hash(MD5Key2 + GetMD5Hash(MD5Key1 + md5));

            return md5;
        }

        private string CalculateResponseMD5Hash(string transact, string currency, string amount)
        {
            var isoCurrency = Iso4217Lookup.LookupByCode(currency);
            if (isoCurrency.Found)
            {
                currency = isoCurrency.Number.ToString();
            }

            var md5 = string.Format(md5ResponseString, transact, amount, currency);
            md5 = GetMD5Hash(MD5Key2 + GetMD5Hash(MD5Key1 + md5));

            return md5;
        }

        internal static string MoneyToString(decimal d)
        {
            return ((int)Math.Round(d * 100, 0, MidpointRounding.AwayFromZero)).ToString();
        }
    }
}