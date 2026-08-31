# OPAY 放款流程测试 / 生产库映射

> 核对时间：2026-08-31（Asia/Shanghai）  
> 展示口径：按业务线作为一级标题；表格保持“服务 / 表 / 库 / 作用”结构。  
> 证据口径：`生产实时` = DBS 只读查询或表结构下拉已验证；`测试缓存` = DataGrip 对象快照已见；`代码配置` = 仓库测试环境路由，不能替代实时库验证。

## 实例总览

| 业务线 | 测试实例 / 数据源 | 生产实例 | 核心库 | Trade / Service 库 | Channel 库 |
|---|---|---|---|---|---|
| ML | DataGrip `ml 110.238.77.34:3306` | Core `10.222.6.130-ease-merchant-loan-core-slave`；Service `10.222.6.88-ease-merchant-loan-service-slave` | 测试 `ml_microloan`；生产 `ml_microloan` | 测试/生产 `ml_ease` | 测试 `ml_pay_channel`；生产 `ml_channel` |
| OK | DataGrip `ok @159.138.174.6:3306`；代码测试地址 `10.220.2.99` | `10.66.1.139-ng-okash-ob` | 测试 `yinni_microloan`；生产 `microloan` | 测试/生产 `okash` | 测试/生产 `pay_channel` |
| EM | DataGrip `em 159.138.165.0:2883`；代码测试地址 `10.220.0.128` | Core `10.224.10.17-ng-em-core-ob-slave`；Service `10.224.10.17-ng-em-service-ob-slave` | 测试 `yinni_microloan`；生产 `microloan` | 测试/生产 `okash` | 测试/生产 `pay_channel` |
| BNPL | DataGrip `bnpl @159.138.165.0:2883` | `10.226.10.147-ng_loan_collect-ob` | 测试 `ep_bill`；生产 `bnpl_ep_bill` | 测试 `ep_pay_trade`；生产 `bnpl_ep_pay_trade` | 测试 `ep_pay_channel`；生产 `bnpl_ep_pay_channel` |

测试实例在 DBS 实例列表可见，但当前账号展开后的数据库列表为空。因此测试侧采用 DataGrip 缓存和代码配置分层标记，不写成“DBS 测试实时”。

# ML

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| Core | `orders` | 测试缓存：`110.238.77.34 / ml_microloan`<br>生产实时：`10.222.6.130 / ml_microloan` | 借款主单、放款和还款状态 |
| Core | `account_base` | 同上 | 借款人账户底座 |
| Core | `orders_fee_float` | 同上 | 申请/放款时费率快照 |
| Core | `core_account_fund_balance_record` | 同上；测试与生产均已见 | 管理费、VAT 等资金变化记录 |
| Core | `order_message_record` | 同上 | MQ 发送记录、补偿和幂等 |
| Core | `repay_plan` | 同上 | 放款成功后生成还款计划 |
| Core | `payment` | 同上 | Core 入账总凭证 |
| Core | `user_e_trans` | 同上 | Core 入账科目明细 |
| Service / Pay Trade | `okash_loan_apply_mark` | 测试缓存：`110.238.77.34 / ml_ease`<br>生产实时：`10.222.6.88 / ml_ease` | 收款银行/钱包快照及申请标记 |
| Service / Pay Trade | `okash_user` | 同上 | 用户主资料；查单时避免输出敏感字段 |
| Pay Trade | `pay_b2c` | 同上 | 放款任务主单；通过 `apply_id`、`b2c_ref` 连接上下游 |
| Service / Pay Trade | `okash_loan_apply` | 同上 | 贷款申请主单及成功后的借款信息 |
| Pay Channel | `pay_channel_out` | 测试缓存：`110.238.77.34 / ml_pay_channel`<br>生产实时：`10.222.6.88 / ml_channel` | 渠道出款聚合主单 |
| Pay Channel | `pay_channel_out_ext` | 同上 | 收款人、币种、sessionId 等扩展信息 |
| Pay Channel | `pay_channel_out_send_log` | 同上 | 每次三方请求、查单和重试 |
| Pay Channel | `pay_channel_out_unusual_trans` | 同上 | FAIL/UNKNOWN 后的异常、重试和关单 |

ML 当前代码和物理表证据未发现 `funds_accounting_info`、`funds_accounting_detail`、`tra_out_transaction`。前两张是 BNPL Bill 表；`tra_out_transaction` 只在旧业务全景描述中出现，当前不能填入 ML 测试或生产库。ML OD 出金/用信表是 `ml_od_trade.tra_transfer_transaction`，不是同一张表。

# OK

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| Core | `orders` | 测试缓存：`159.138.174.6 / yinni_microloan`；代码配置：`10.220.2.99 / yinni_microloan`<br>生产实时：`10.66.1.139 / microloan` | 借款主单、放款和还款状态 |
| Core | `account_base` | 同上 | 借款人账户底座 |
| Core | `orders_fee_float` | 同上 | 申请/放款时费率快照 |
| Core | `core_account_fund_balance_record` | 测试缓存已见：`yinni_microloan`<br>生产路由：`microloan`，但 DBS 当前表结构未见 | 管理费、VAT 等资金变化记录；存在版本/部署差异 |
| Core | `order_message_record` | 测试缓存：`yinni_microloan`<br>生产实时：`microloan` | MQ 发送记录、补偿和幂等 |
| Core | `repay_plan` | 同上 | 放款成功后生成还款计划 |
| Core | `payment` | 同上 | Core 入账总凭证 |
| Core | `user_e_trans` | 同上 | Core 入账科目明细 |
| Service / Pay Trade | `okash_loan_apply_mark` | 测试缓存：`159.138.174.6 / okash`；代码配置：`10.220.2.99 / okash`<br>生产实时：`10.66.1.139 / okash` | 收款银行/钱包快照及申请标记 |
| Service / Pay Trade | `okash_user` | 同上 | 手机号、用户 UUID；查单时避免输出敏感字段 |
| Pay Trade | `pay_b2c` | 同上 | 放款任务主单 |
| Service / Pay Trade | `okash_loan_apply` | 同上 | 贷款申请主单及成功后的借款信息 |
| Pay Channel | `pay_channel_out` | 测试缓存：`159.138.174.6 / pay_channel`<br>生产实时：`10.66.1.139 / pay_channel` | 渠道出款聚合主单 |
| Pay Channel | `pay_channel_out_ext` | 同上 | 收款人、币种、sessionId 等扩展信息 |
| Pay Channel | `pay_channel_out_send_log` | 同上 | 每次三方请求、查单和重试 |
| Pay Channel | `pay_channel_out_unusual_trans` | 同上 | FAIL/UNKNOWN 后的异常、重试和关单 |

OK 不使用本清单中的两张 `funds_accounting_*` 表；`tra_out_transaction` 也没有当前源码、DataGrip 或 DBS 物理表证据。

# EM

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| Core | `orders` | 测试缓存：`159.138.165.0 / yinni_microloan`；代码配置：`10.220.0.128 / yinni_microloan`<br>生产实时：`10.224.10.17-ng-em-core-ob-slave / microloan` | 借款主单、放款和还款状态 |
| Core | `account_base` | 同上 | 借款人账户底座 |
| Core | `orders_fee_float` | 同上 | 申请/放款时费率快照 |
| Core | `core_account_fund_balance_record` | 测试缓存已见：`yinni_microloan`<br>生产路由：`microloan`，但 DBS 当前表结构未见 | 管理费、VAT 等资金变化记录；存在版本/部署差异 |
| Core | `order_message_record` | 测试缓存：`yinni_microloan`<br>生产实时：`microloan` | MQ 发送记录、补偿和幂等 |
| Core | `repay_plan` | 同上 | 放款成功后生成还款计划 |
| Core | `payment` | 同上 | Core 入账总凭证 |
| Core | `user_e_trans` | 同上 | Core 入账科目明细 |
| Service / Pay Trade | `okash_loan_apply_mark` | 测试缓存：`159.138.165.0 / okash`；代码配置：`10.220.0.128 / okash`<br>生产实时：`10.224.10.17-ng-em-service-ob-slave / okash` | 收款银行/钱包快照及申请标记 |
| Service / Pay Trade | `okash_user` | 同上 | 手机号、用户 UUID；查单时避免输出敏感字段 |
| Pay Trade | `pay_b2c` | 同上 | 放款任务主单 |
| Service / Pay Trade | `okash_loan_apply` | 同上 | 贷款申请主单及成功后的借款信息 |
| Pay Channel | `pay_channel_out` | 测试缓存：`159.138.165.0 / pay_channel`<br>生产实时：`10.224.10.17-ng-em-service-ob-slave / pay_channel` | 渠道出款聚合主单 |
| Pay Channel | `pay_channel_out_ext` | 同上 | 收款人、币种、sessionId 等扩展信息 |
| Pay Channel | `pay_channel_out_send_log` | 同上 | 每次三方请求、查单和重试 |
| Pay Channel | `pay_channel_out_unusual_trans` | 同上 | FAIL/UNKNOWN 后的异常、重试和关单 |

EM 不使用本清单中的两张 `funds_accounting_*` 表；`tra_out_transaction` 也没有当前源码、DataGrip 或 DBS 物理表证据。

# BNPL

BNPL 不是传统借据模型，不能直接套用 `orders -> pay_b2c -> repay_plan`。下表按你给出的 19 个概念逐项对应 BNPL 的实际表。

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| Trade | `tra_consume_transaction`、`tra_installment_transaction` | 测试缓存：`159.138.165.0 / ep_pay_trade`<br>生产实时：`10.226.10.147 / bnpl_ep_pay_trade` | 对应传统 `orders` 的消费/分期交易入口；提现另看 `tra_withdrawal_transaction` |
| Bill | `bill_account_info` | 测试缓存：`ep_bill`<br>生产实时：`bnpl_ep_bill` | 账务账户、规则和账期参数；不是身份账户 `account_base` |
| Bill | `bill_rule_detail`、`bill_fee_transaction` | 同上 | 对应费率规则和费用入账事实，不是 `orders_fee_float` 的一对一复制 |
| Bill | `funds_accounting_info` | 测试库路由：`ep_bill`，2026-08-03 旧缓存未见<br>生产实时：`bnpl_ep_bill`，100 个物理分片 | 新清结算/资金记账主表 |
| Bill | `funds_accounting_detail` | 同上 | 新清结算逐科目资金明细 |
| Bill | `order_message_record` | 测试缓存：`ep_bill`<br>生产实时：`bnpl_ep_bill` | MQ 发送记录与补偿；与传统 Core 同名但数据域独立 |
| Bill | `bill_installment_plan`、`user_bill_into_account`、`user_bill_out_account`、`user_bill_detail` | 测试缓存：`ep_bill`<br>生产实时：`bnpl_ep_bill` | 对应传统 `repay_plan` 的分期计划、未出账/已出账应收和交易桥接 |
| Trade / Bill | `tra_repayment_order`、`bill_repay_transaction` | 测试缓存：`ep_pay_trade` + `ep_bill`<br>生产实时：`bnpl_ep_pay_trade` + `bnpl_ep_bill` | 对应一次还款支付单和 Bill 入账，不使用传统 `payment` |
| Bill | `user_bill_detail`、`funds_accounting_detail` | 测试缓存/路由：`ep_bill`<br>生产实时：`bnpl_ep_bill` | 对应账单分配和资金科目明细，不使用传统 `user_e_trans` |
| Trade | `tra_withdrawal_transaction` | 测试缓存：`ep_pay_trade`<br>生产实时：`bnpl_ep_pay_trade` | CashBack 提现/出金交易；当前不存在精确名为 `tra_out_transaction` 的表 |
| Trade / 外部 User 域 | 无 `okash_loan_apply_mark`、`okash_user`、`okash_loan_apply` 直接对应表 | 当前三仓不适用 | BNPL 消费/分期直接以 `tra_*` 交易为入口；用户主档属于外部 User 域 |
| Trade / Channel | `tra_withdrawal_transaction`、`pay_channel_opay_cash_back` | 测试缓存：`ep_pay_trade` + `ep_pay_channel`<br>生产实时：`bnpl_ep_pay_trade` + `bnpl_ep_pay_channel` | 对应 CashBack 提现；普通消费不走传统 `pay_b2c` |
| Channel | `pay_channel_out` | 测试缓存：`ep_pay_channel`<br>生产实时：`bnpl_ep_pay_channel` | 渠道出款事实 |
| Channel | 无 `pay_channel_out_ext` | 当前代码与生产表结构均未见 | BNPL Channel 当前没有一对一扩展表 |
| Channel | 无 `pay_channel_out_send_log` | 当前代码与生产表结构均未见 | 可查 `channel_mq_exception` 的消息异常，但它不是发送日志等价表 |
| Channel | 无 `pay_channel_out_unusual_trans` | 当前代码与生产表结构均未见 | `channel_mq_exception` 只能作为异常消息线索，不能当作异常交易等价表 |

BNPL 生产当前还有一个明确部署缺口：代码和旧测试缓存存在 `bill_repay_trans_detail`，但 `bnpl_ep_bill` 当前在线表结构未见。查单应先使用 `bill_repay_transaction + user_bill_detail`，不要假定缺失表可在线查询。

# 核对结论

1. ML、OK、EM 的放款主链分成三个库域：Core、Service/Pay Trade、Pay Channel；同一业务线也不能跨库直接联表。
2. ML 的测试 Channel 库名是 `ml_pay_channel`，生产是 `ml_channel`；不能仅凭名称相似把它们当成同一物理库。
3. OK/EM 的 `core_account_fund_balance_record` 在测试缓存存在，但生产 DBS 当前未见，必须保留为部署差异。
4. `funds_accounting_info`、`funds_accounting_detail` 当前属于 BNPL Bill，不属于 ML、OK、EM Core。
5. `tra_out_transaction` 当前无法映射到任何已确认测试/生产物理库；不能为填满表格而外推。
