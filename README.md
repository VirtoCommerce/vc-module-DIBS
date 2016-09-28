# DIBS Account payment gateway module
DIBS module provides integration with <a href="http://www.dibspayment.com" target="_blank">DIBS Account</a> payment gateway. 

# Installation
Installing the module:
* Automatically: in VC Manager go to Configuration -> Modules -> DiBs payment gateway -> Install
* Manually: download module zip package from https://github.com/VirtoCommerce/vc-module-DIBS/releases. In VC Manager go to Configuration -> Modules -> Advanced -> upload module package -> Install.

# Module management and settings UI
![image](https://cloud.githubusercontent.com/assets/5801549/16379653/76ba16dc-3c7b-11e6-80aa-b11fdf76abe5.png)

# Settings
* **DIBS FlexWin URL** - DIBS redirect url to FlexWin
* **Accept URL** - If the payment is accepted and the customer is leaving FlexWin, redirect to this URL. Provide absolute URL to your {storefront URL}/cart/externalpaymentcallback
* **Callback URL** - Automatic callback URL after a payment was authorized with DIBS. Provide absolute URL to your {VC manager URL}/api/dibs/callback
* **Merchant id** - DIBS provided merchant id
* **MD5 Key1** - MD5 Key1 can be found in DIBS Admin: Integration / MD5 Keys
* **MD5 Key2** - MD5 Key2 can be found in DIBS Admin: Integration / MD5 Keys
* **Mode** - Set payment mode. 'test' for test purposes. 'live' - production setting.
* **Form Design** - Payment form design available from DIBS. Possible values are "default", "basal", "rich" and "responsive"

# DIBS configuration
These settings on DIBS Administration site must be configured as following:
* Integration > FlexWin > FlexWin settings > "**Skip accept page**" has to be checked
* Integration > Return Values > "**Order ID**" has to be checked
* Integration > Return Values > "**All fields exclusive of card information** ..." has to be checked



# License
Copyright (c) Virtosoftware Ltd.  All rights reserved.

Licensed under the Virto Commerce Open Software License (the "License"); you
may not use this file except in compliance with the License. You may
obtain a copy of the License at

http://virtocommerce.com/opensourcelicense

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
implied.
