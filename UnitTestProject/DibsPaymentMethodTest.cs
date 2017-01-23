using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VirtoCommerce.Domain.Order.Model;
using VirtoCommerce.Domain.Payment.Model;
using VirtoCommerce.Platform.Core.Settings;
using DiBs.Managers;
using RestSharp.Extensions.MonoHttp;
using System.Collections.Specialized;
using VirtoCommerce.Platform.Core.DynamicProperties;

namespace UnitTestProject
{
    [TestClass]
    public class DibsPaymentMethodTest
    {
        private string MerchantId = "12345678";
        private string MD5Key1 = "";
        private string MD5Key2 = "";
        private string LoginApiUser = "";
        private string PasswordApiUser = "";

        private string amount = "2000";
        private string currencyCode = "208";
        private string orderId = "dibs_test_01";
        private string transact = "789789789";

        private DibsPaymentMethod PaymentMethod => GetPaymentMethod();

        [TestMethod]
        public void PaymentTest()
        {
            var param = new NameValueCollection
            {
                {"accepturl", "http://localhost/store/cart/externalpaymentcallback"},
                {"amount", amount},
                {"callbackurl", "http://localhost/admin/api/dibs/callback"},
                {"currency", currencyCode},
                {"merchant", MerchantId},
                {"orderid", orderId},
                {"decorator", "responsive"}
            };
            param.Add("md5key", PaymentMethod.CalculateMD5Hash($"merchant={MerchantId}&orderid={param["orderid"]}&currency={param["currency"]}&amount={param["amount"]}"));
            param.Add("test", "1");

            var requestUrl = "https://payment.architrade.com/paymentweb/start.action";
            //string res = ProcessRequest(param, requestUrl);

            //build form to post to FlexWin
            var checkoutform = $"<form name='dibs' action='{requestUrl}' method='POST' charset='UTF-8'>";

            foreach (string key in param)
                checkoutform += $"<INPUT TYPE='hidden' name='{key}' value='{param[key]}'>";
            checkoutform += "<button type='submit'>Proceed</button></form><script language='javascript'>document.dibs.submit();</script>";

            Assert.IsNotNull(checkoutform);
        }

        [TestMethod]
        public void ReauthPaymentTest()
        {
            /*<form method="post" action="https://payment.architrade.com/cgi-bin/reauth.cgi">
                <input name="merchant" value="98765432" type="hidden" />
                <input name="transact" value="789789789" type="hidden" />
                <input name="textreply" value="yes" type="hidden" />
            </form>*/

            var param = new NameValueCollection
            {
                {"merchant", MerchantId},
                {"transact", transact},
                {"textreply", "yes"}
            };

            var requestUrl = "https://payment.architrade.com/cgi-bin/reauth.cgi";
            string res = ProcessRequest(param, requestUrl);

            Assert.IsTrue(res.StartsWith("status=ACCEPTED"));

            /*  0 - Rejected by acquirer.
                1 - Communication problems.
                2 - Error in the parameters sent to the DIBS server.
                3 - Error at the acquirer.
                4 - Credit card expired.
                5 - Your shop does not support this credit card type, the credit card type could not be identified, or the credit card number was not modulus correct.
                6 - Instant capture failed.
                7 - The order number (orderid) is not unique.
                8 - There number of amount parameters does not correspond to the number given in the split parameter.
                9 - Control numbers (cvc) are missing.
                10 - The credit card does not comply with the credit card type.
                11 - Declined by DIBS Defender.
                20 - Cancelled by user at 3D Secure authentication step.*/
        }

        [TestMethod]
        public void CancelTest()
        {
            /*<form method="post" action="https://login:password@payment.architrade.com/cgi-adm/cancel.cgi">
              <input type="hidden" name="merchant" value="98765432" />
              <input type="hidden" name="transact" value="12345678" />
              <input type="hidden" name="orderid" value="dibs_test_01">
              <input type="hidden" name="md5key" value="cfcd208495d565ef66e7dff9f98764da" />
              <input type="hidden" name="textreply" value="yes" />
            </form>*/

            var param = new NameValueCollection
            {
                {"merchant", MerchantId},
                {"transact", transact},
                {"orderid", orderId},
                {"textreply", "yes"}
            };
            param.Add("md5key", PaymentMethod.CalculateMD5Hash($"merchant={MerchantId}&orderid={param["orderid"]}&transact={param["transact"]}"));

            var requestUrl = $"https://payment.architrade.com/cgi-adm/cancel.cgi";
            string res = ProcessRequest(param, requestUrl, true);

            Assert.IsTrue(res.StartsWith("status=ACCEPTED"));
        }

        [TestMethod]
        public void PaymentCancelTest()
        {
            var context = new VoidProcessPaymentEvaluationContext
            {
                Payment = new PaymentIn
                {
                    PaymentStatus = PaymentStatus.Authorized,
                    OuterId = transact
                },
                Order = new CustomerOrder
                {
                    Number = orderId
                }
            };

            var result = PaymentMethod.VoidProcessPayment(context);

            Assert.IsTrue(result.NewPaymentStatus == PaymentStatus.Cancelled);
        }

        [TestMethod]
        public void CaptureTest()
        {
            /*<form method="post" action=https://payment.architrade.com/cgi-bin/capture.cgi>
              <input type="hidden" name="merchant" value="12345678">
              <input type="hidden" name="amount" value="2000">
              <input type="hidden" name="transact" value="1234567">
              <input type="hidden" name="orderid" value="11223344">
            </form>*/

            var param = new NameValueCollection
            {
                {"merchant", MerchantId},
                {"amount", amount},
                {"transact", transact},
                {"orderid", orderId}
            };
            param.Add("md5key", PaymentMethod.CalculateMD5Hash($"merchant={MerchantId}&orderid={param["orderid"]}&transact={param["transact"]}&amount={param["amount"]}"));

            var requestUrl = $"https://payment.architrade.com/cgi-bin/capture.cgi";
            string res = ProcessRequest(param, requestUrl);

            Assert.IsTrue(res.StartsWith("status=ACCEPTED"));
        }

        [TestMethod]
        public void PaymentCaptureTest()
        {
            var context = new CaptureProcessPaymentEvaluationContext
            {
                Payment = new PaymentIn
                {
                    Sum = 0.01m,
                    PaymentStatus = PaymentStatus.Authorized,
                    OuterId = transact
                },
                Order = new CustomerOrder
                {
                    Number = orderId
                }
            };

            var result = PaymentMethod.CaptureProcessPayment(context);

            Assert.IsTrue(result.NewPaymentStatus == PaymentStatus.Paid);
        }

        [TestMethod]
        public void RefundTest()
        {
            /*<form method="post" action="https://login:password@payment.architrade.com/cgi-adm/refund.cgi">
                <input type="hidden" name="merchant" value="12345678">
                <input type="hidden" name="transact" value="11111111">
                <input type="hidden" name="amount" value="2000">
                <input type="hidden" name="currency" value="208">
                <input type="hidden" name="orderid" value="11223344">
                <input type="hidden" name="md5key" value="cfcd208495d565ef66e7dff9f98764da">
                <input type="hidden" name="textreply" value="yes">
            </form>*/

            var param = new NameValueCollection
            {
                {"merchant", MerchantId},
                {"transact", transact},
                {"amount", amount},
                {"currency", currencyCode},
                {"orderid", orderId},
                {"textreply", "yes"}
            };
            param.Add("md5key", PaymentMethod.CalculateMD5Hash($"merchant={MerchantId}&orderid={param["orderid"]}&transact={param["transact"]}&amount={param["amount"]}"));

            var requestUrl = $"https://payment.architrade.com/cgi-adm/refund.cgi";
            string res = ProcessRequest(param, requestUrl, true);

            Assert.IsTrue(res.StartsWith("status=ACCEPTED"));
        }

        [TestMethod]
        public void PaymentRefundTest()
        {
            var context = new RefundProcessPaymentEvaluationContext
            {
                Payment = new PaymentIn
                {
                    Sum = 0.01m,
                    IsApproved = true,
                    PaymentStatus = PaymentStatus.Paid,
                    OuterId = transact,
                    Currency = currencyCode
                },
                Order = new CustomerOrder
                {
                    Number = orderId
                }
            };

            var result = PaymentMethod.RefundProcessPayment(context);

            Assert.IsTrue(result.NewPaymentStatus == PaymentStatus.Refunded);
        }

        private string ProcessRequest(NameValueCollection param, string url, bool useApiCredential = false)
        {
            var encoding = Encoding.GetEncoding("ISO-8859-1");
            string requestData = string.Join("&", (from string name in param select string.Concat(name, "=", HttpUtility.UrlEncode(param[name], encoding))).ToArray());
            byte[] data = encoding.GetBytes(requestData);

            HttpWebRequest myRequest = (HttpWebRequest)WebRequest.Create(url);
            myRequest.Method = "POST";
            myRequest.ContentType = "application/x-www-form-urlencoded";

            if (useApiCredential)
                myRequest.Credentials = new NetworkCredential(LoginApiUser, PasswordApiUser);

            myRequest.ContentLength = data.Length;
            Stream newStream = myRequest.GetRequestStream();
            newStream.Write(data, 0, data.Length);
            newStream.Close();

            WebResponse myResponse = myRequest.GetResponse();
            String response = String.Empty;
            using (StreamReader sr = new StreamReader(myResponse.GetResponseStream()))
            {
                response = sr.ReadToEnd();
                sr.Close();
            }

            return response;
        }

        private DibsPaymentMethod GetPaymentMethod()
        {
            var settings = new Collection<SettingEntry>
            {
                new SettingEntry
                {
                    Name = "DiBs.RedirectUrl",
                    ValueType = SettingValueType.ShortText,
                    Value = "https://payment.architrade.com/paymentweb/start.action"
                },
                new SettingEntry
                {
                    Name = "DiBs.AcceptUrl",
                    Value = "http://localhost/frontend/cart/externalpaymentcallback"
                },
                new SettingEntry
                {
                    Name = "DiBs.CallbackUrl",
                    Value = "http://localhost/admin/api/dibs/callback"
                },
                new SettingEntry
                {
                    Name = "DiBs.MerchantId",
                    Value = MerchantId
                },
                new SettingEntry
                {
                    Name = "DiBs.MD5Key1",
                    Value = MD5Key1
                },
                new SettingEntry
                {
                    Name = "DiBs.MD5Key2",
                    Value = MD5Key2
                },
                new SettingEntry
                {
                    Name = "DiBs.Mode",
                    Value = "test"
                },
                new SettingEntry
                {
                    Name = "DiBs.FormDesign",
                    Value = "responsive"
                },
                new SettingEntry
                {
                    Name = "DiBs.ApiUser.Login",
                    Value = LoginApiUser
                },
                new SettingEntry
                {
                    Name = "DiBs.ApiUser.Password",
                    Value = PasswordApiUser
                }
            };

            var retVal = new DibsPaymentMethod("DIBS")
            {
                Settings = settings
            };

            return retVal;
        }
    }
}
