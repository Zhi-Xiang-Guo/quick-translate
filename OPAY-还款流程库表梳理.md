**OPAY 还款流程库表梳理**

> 核验时间：2026-08-31（Asia/Shanghai）  
> 范围：ML（含传统借据与 OD）、OK、EM、BNPL。  
> 格式：按业务线作为一级标题；每张主表按“服务 / 表 / 库 / 作用”展开。  
> 证据标记：`生产在线` = DBS 已实时确认实例、库和表；`测试缓存` = DataGrip 已缓存表结构；`代码配置` = 仓内环境配置，未实时连接；`实例-only` = DBS 只看到实例，当前账号无法展开数据库。

重要边界：测试库的 DataGrip 地址与代码中的 `10.220.*` 地址可能是不同测试环境，也可能是同一环境的不同入口；没有网络/DBA 证据前不合并。生产以 DBS 在线结果为准，不以仓内旧 profile 覆盖。

# 1. ML（Merchant Loan）

ML 有两套还款模型：传统固定期限借据使用 `orders + repay_plan`；ML OD 使用 `tra_repayment_order + bill_*`，两者不能混查。

## 1.1 传统借据

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| core | `orders` | 测试缓存：`110.238.77.34:3306 / ml_microloan`；代码配置：`10.220.0.128:3306 / ml_microloan`<br>生产在线：`10.222.6.130-ease-merchant-loan-core-slave / ml_microloan` | 借据主单和整体还款状态；业务申请号在 `orders.order_id`，内部关联主键是 `orders.id` |
| core | `repay_plan` | 同上 | 分期应收、已还、减免、罚息和结清事实 |
| pay-trade | `pay_repayment_order` | 测试缓存：`110.238.77.34:3306 / ml_ease`<br>生产在线：`10.222.6.88-ease-merchant-loan-service-slave / ml_ease` | 一次用户还款支付总单；支付成功不等于 Core 已入账 |
| pay-channel | `pay_channel_in` | 测试缓存：`110.238.77.34:3306 / ml_pay_channel`<br>生产在线：`10.222.6.88-ease-merchant-loan-service-slave / ml_channel` | 渠道入金主单、三方流水、渠道状态和失败原因 |
| pay-channel | `pay_channel_in_ext` | 同上 | 入金扩展信息，如付款人、渠道参数和回调辅助字段 |
| pay-trade | `pay_repayment_core_detail` | 测试缓存：`110.238.77.34:3306 / ml_ease`<br>生产在线：`10.222.6.88-ease-merchant-loan-service-slave / ml_ease` | 将还款总单按借据拆分，保存 Core 批次号、入账金额和处理状态 |
| core | `payment` | 测试缓存：`110.238.77.34:3306 / ml_microloan`；代码配置：`10.220.0.128:3306 / ml_microloan`<br>生产在线：`10.222.6.130-ease-merchant-loan-core-slave / ml_microloan` | Core 入账总账凭证；`batch_no` 对应 Trade 的 `repayment_core_no` |
| core | `user_e_trans` | 同上 | Core 科目明细账，记录本金、利息、服务费和罚息等冲抵 |
| core | `reduce_record` | 同上 | 减免流水；不能与现金实收混为同一事实 |
| core | `account_balance` | 同上 | 用户多还余额和退款中冻结余额当前值 |
| core | `account_balance_flow` | 同上 | 多还余额增加、使用、冻结和释放流水 |
| core | `order_message_record` | 同上 | Core MQ 发送记录、状态和补发检查点 |
| pay-trade | `bluridge_mq_consume_result` | 测试缓存：`110.238.77.34:3306 / ml_ease`<br>生产在线：`10.222.6.88-ease-merchant-loan-service-slave / ml_ease` | Trade 消费 Core/支付事件的执行结果和补偿检查点 |
| pay-trade | `pay_repayment_overpaid` | 同上 | Trade 侧多还处理记录；不能替代 Core 的 `account_balance*` |
| pay-channel | `pay_channel_in_notify_unusual` | 测试缓存：`110.238.77.34:3306 / ml_pay_channel`<br>生产在线：`10.222.6.88-ease-merchant-loan-service-slave / ml_channel` | 无法正常匹配入金单的异常通知和人工处理记录 |

主链单号关系：

```text
ml_channel.pay_channel_in.bus_order_id
= ml_ease.pay_repayment_order.repayment_order_no
= ml_ease.pay_repayment_core_detail.repayment_order_no

pay_repayment_core_detail.apply_id
= ml_microloan.orders.order_id

pay_repayment_core_detail.repayment_core_no
= ml_microloan.payment.batch_no
= ml_microloan.user_e_trans.batch_no
```

`110.238.77.34 / yinni_microloan` 只保留为历史/Batch 测试候选；当前生产已实时确认为 `ml_microloan`，不得合并成同一生产库。

## 1.2 ML OD

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| ml-od-trade | `tra_repayment_order` | 测试缓存：`110.238.77.34:3306 / ml_od_trade`<br>生产在线：`10.222.6.88-ease-merchant-loan-service-slave / ml_od_trade` | 主动/自动还款业务单，保存支付状态、`bill_flag`、支付时间和额度释放动作号 |
| pay-channel | `pay_channel_in` | 测试缓存：`110.238.77.34:3306 / ml_pay_channel`<br>生产在线：`10.222.6.88-ease-merchant-loan-service-slave / ml_channel` | OD 渠道入金；必须同时确认 `biz_type=OD` |
| ml-bill | `bill_repay_transaction` | 测试缓存：`110.238.77.34:3306 / ml_bill`<br>生产在线：`10.222.6.130-ease-merchant-loan-core-slave / ml_bill` | Bill 还款入账主交易和入账状态 |
| ml-bill | `bill_post_plan` | 同上 | 未出账应收计划，记录各类应收、已还、减免和退款累计 |
| ml-bill | `bill_post_plan_detail` | 同上 | 未出账应收计划对应的交易级明细 |
| ml-bill | `bill_statement_info` | 同上 | 已出账 statement 聚合、账期金额和状态 |
| ml-bill | `bill_statement_trans` | 同上 | 已出账 statement 的交易清单和还款分配事实 |
| ml-bill | `bill_account_info` | 同上 | 循环账户、账期指针和当前账户状态 |
| ml-bill | `bill_mq_exception` | 同上 | Bill MQ 异常和补偿检查点 |
| ml-od-trade | `tra_mq_exception` | 测试缓存：`110.238.77.34:3306 / ml_od_trade`<br>生产在线：`10.222.6.88-ease-merchant-loan-service-slave / ml_od_trade` | OD Trade MQ 异常和补偿检查点 |

```text
ml_od_trade.tra_repayment_order.trans_no
= ml_channel.pay_channel_in.bus_order_id
= ml_bill.bill_repay_transaction.repay_trans_no
= ml_bill.bill_statement_trans.trans_no
```

完成判断不能只看 `repayment_order_sts=1`；还需核对 `bill_flag=1`、`bill_repay_transaction` 和 PostPlan/Statement 明细。

# 2. OK

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| service-ng / pay-trade | `okash_loan_apply` | 测试缓存：`159.138.174.6:3306 / okash`；代码配置：`10.220.2.99:3306 / okash`<br>生产在线：`10.66.1.139-ng-okash-ob / okash` | 用信申请主单；还款完成后更新 `is_cleared/cleared_time` |
| core | `orders` | 测试缓存：`159.138.174.6:3306 / yinni_microloan`；代码配置：`10.220.2.99:3306 / yinni_microloan`<br>生产在线：`10.66.1.139-ng-okash-ob / microloan` | 借据主单和整体还款状态 |
| core | `repay_plan` | 同上 | 分期应收、已还、减免、罚息和结清事实 |
| pay-trade | `pay_repayment_order` | 测试缓存：`159.138.174.6:3306 / okash`；代码配置：`10.220.2.99:3306 / okash`<br>生产在线：`10.66.1.139-ng-okash-ob / okash` | 一次用户还款总单及支付状态 |
| pay-channel | `pay_channel_in` | 测试缓存：`159.138.174.6:3306 / pay_channel`；代码测试实例由 Apollo 管理，待确认<br>生产在线：`10.66.1.139-ng-okash-ob / pay_channel` | 渠道入金主单、三方流水、渠道状态和失败原因 |
| pay-channel | `pay_channel_in_ext` | 同上 | 入金扩展信息，如付款人、渠道参数和回调辅助字段 |
| pay-trade | `pay_repayment_core_detail` | 测试缓存：`159.138.174.6:3306 / okash`；代码配置：`10.220.2.99:3306 / okash`<br>生产在线：`10.66.1.139-ng-okash-ob / okash` | 还款总单按借据拆分，保存 Core 批次号、入账金额和状态 |
| core | `payment` | 测试缓存：`159.138.174.6:3306 / yinni_microloan`；代码配置：`10.220.2.99:3306 / yinni_microloan`<br>生产在线：`10.66.1.139-ng-okash-ob / microloan` | Core 还款入账总账凭证 |
| core | `user_e_trans` | 同上 | Core 科目明细账，本金、利息、服务费、罚息等拆分 |
| core | `reduce_record` | 同上 | 提前结清、优惠券等场景产生的减免流水 |
| core | `account_balance` | 同上 | 用户多还余额和退款中冻结余额当前值 |
| core | `account_balance_flow` | 同上 | 多还余额增加、使用、冻结和释放流水 |
| core | `order_message_record` | 同上 | Core 还款成功 MQ 发送记录和失败补发 |
| pay-trade | `bluridge_mq_consume_result` | 测试缓存：`159.138.174.6:3306 / okash`；代码配置：`10.220.2.99:3306 / okash`<br>生产在线：`10.66.1.139-ng-okash-ob / okash` | `paid_success` 等事件消费结果和补偿检查点 |
| pay-trade | `pay_repayment_overpaid` | 同上 | Trade 侧多还异常/待处理记录 |
| pay-channel | `pay_channel_in_notify_unusual` | 测试缓存：`159.138.174.6:3306 / pay_channel`；代码测试实例由 Apollo 管理，待确认<br>生产在线：`10.66.1.139-ng-okash-ob / pay_channel` | 无法匹配正常入金单的异常通知、转账或回调记录 |

```text
okash.pay_repayment_order.repayment_order_no
= pay_channel.pay_channel_in.bus_order_id

okash.pay_repayment_core_detail.repayment_core_no
= microloan.payment.batch_no
= microloan.user_e_trans.batch_no
```

# 3. EM（Easemoni）

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| service-ng / pay-trade | `okash_loan_apply` | 测试缓存：`159.138.165.0:2883 / okash`；代码配置：`10.220.0.128:3306 / okash`<br>生产在线：`10.224.10.17-ng-em-service-ob-slave / okash` | 用信申请主单；还款完成后更新结清标记 |
| core | `orders` | 测试缓存：`159.138.165.0:2883 / yinni_microloan`；代码配置：`10.220.0.128:3306 / yinni_microloan`<br>生产在线：`10.224.10.17-ng-em-core-ob-slave / microloan` | 借据主单和整体还款状态 |
| core | `repay_plan` | 同上 | 分期应收、已还、减免、罚息和结清事实 |
| pay-trade | `pay_repayment_order` | 测试缓存：`159.138.165.0:2883 / okash`；代码配置：`10.220.0.128:3306 / okash`<br>生产在线：`10.224.10.17-ng-em-service-ob-slave / okash` | 一次用户还款总单及支付状态 |
| pay-channel | `pay_channel_in` | 测试缓存：`159.138.165.0:2883 / pay_channel`；代码测试实例由 Apollo 管理，待确认<br>生产在线：`10.224.10.17-ng-em-service-ob-slave / pay_channel` | 渠道入金主单、三方流水、渠道状态和失败原因 |
| pay-channel | `pay_channel_in_ext` | 同上 | 入金扩展信息，如付款人、渠道参数和回调辅助字段 |
| pay-trade | `pay_repayment_core_detail` | 测试缓存：`159.138.165.0:2883 / okash`；代码配置：`10.220.0.128:3306 / okash`<br>生产在线：`10.224.10.17-ng-em-service-ob-slave / okash` | 还款总单按借据拆分，保存 Core 批次号、入账金额和状态 |
| core | `payment` | 测试缓存：`159.138.165.0:2883 / yinni_microloan`；代码配置：`10.220.0.128:3306 / yinni_microloan`<br>生产在线：`10.224.10.17-ng-em-core-ob-slave / microloan` | Core 还款入账总账凭证 |
| core | `user_e_trans` | 同上 | Core 科目明细账，本金、利息、服务费、罚息等拆分 |
| core | `reduce_record` | 同上 | 提前结清、优惠券等场景产生的减免流水 |
| core | `account_balance` | 同上 | 用户多还余额和退款中冻结余额当前值 |
| core | `account_balance_flow` | 同上 | 多还余额增加、使用、冻结和释放流水 |
| core | `order_message_record` | 同上 | Core 还款成功 MQ 发送记录和失败补发 |
| pay-trade | `bluridge_mq_consume_result` | 测试缓存：`159.138.165.0:2883 / okash`；代码配置：`10.220.0.128:3306 / okash`<br>生产在线：`10.224.10.17-ng-em-service-ob-slave / okash` | `paid_success` 等事件消费结果和补偿检查点 |
| pay-trade | `pay_repayment_overpaid` | 同上 | Trade 侧多还异常/待处理记录 |
| pay-channel | `pay_channel_in_notify_unusual` | 测试缓存：`159.138.165.0:2883 / pay_channel`；代码测试实例由 Apollo 管理，待确认<br>生产在线：`10.224.10.17-ng-em-service-ob-slave / pay_channel` | 无法匹配正常入金单的异常通知、转账或回调记录 |

```text
okash.pay_repayment_order.repayment_order_no
= pay_channel.pay_channel_in.bus_order_id

okash.pay_repayment_core_detail.repayment_core_no
= microloan.payment.batch_no
= microloan.user_e_trans.batch_no
```

# 4. BNPL

BNPL 不使用传统 `orders + repay_plan + payment + user_e_trans` 模型。还款主链是 Trade → Channel → Bill。

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| ease-pay-trade | `tra_repayment_order` | 测试缓存：`159.138.165.0:2883 / ep_pay_trade`<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_pay_trade` | 主动/自动还款业务单，保存支付状态、`bill_flag`、支付时间和来源 |
| ease-pay-channel | `pay_channel_in` | 测试缓存：`159.138.165.0:2883 / ep_pay_channel`<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_pay_channel` | 渠道入金主单、三方流水和渠道结果；查询分片时需同时带 `user_id` |
| ease-pay-bill | `bill_repay_transaction` | 测试缓存：`159.138.165.0:2883 / ep_bill`；代码配置：`10.220.0.128:3306 / ep_bill`<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_bill` | Bill 收到的还款主交易和 `POSTED/NOT_POSTED` 入账状态 |
| ease-pay-bill | `user_bill_detail` | 同上 | 当前生产可查询的账单与来源交易桥接明细，也是当前线上查单的分账入口 |
| ease-pay-bill | `bill_post_detail` | 同上 | Bill 公共入账明细，记录分到账单及各科目 paid、remain、balance |
| ease-pay-bill | `bill_repay_trans_detail` | 测试缓存：`159.138.165.0:2883 / ep_bill`；代码 DDL/分片规则存在<br>生产：`bnpl_ep_bill` 当前在线表结构未见 | 旧链路按账单拆分的还款明细；当前主链已停止写入，Batch 迁移仍读取；不得作为当前线上必查表 |
| ease-pay-bill | `user_bill_out_account` | 测试缓存：`159.138.165.0:2883 / ep_bill`；代码配置：`10.220.0.128:3306 / ep_bill`<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_bill` | 已出账应收、五类已还/减免/退款累计和账单状态 |
| ease-pay-bill | `bill_account_balance` | 测试：当前代码/分片配置已使用；旧 DataGrip 缓存需刷新<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_bill` | 当前主还款链的用户多还余额当前值 |
| ease-pay-bill | `bill_account_balance_op_detail` | 同上 | 当前多还余额增加、使用、冻结等操作明细和幂等业务号 |
| ease-pay-bill | `user_overpaid_balance` | 测试缓存：`159.138.165.0:2883 / ep_bill`<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_bill` | 旧多还余额当前值；保留用于历史数据/兼容，不作为当前主链唯一事实 |
| ease-pay-bill | `user_overpaid_balance_detail` | 同上 | 旧多还余额变动明细；当前链需优先核对 `bill_account_balance*` |
| ease-pay-bill | `bill_reduce_transaction` | 同上 | 小额尾差、运营减免等减免主交易 |
| ease-pay-bill | `bill_reduce_record` | 同上 | 减免分配到账单和科目的明细 |
| ease-pay-bill | `funds_accounting_info` | 测试：DataGrip 旧缓存未见；当前代码已使用<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_bill` | 新资金记账主表；生产为 100 分片表族 |
| ease-pay-bill | `funds_accounting_detail` | 同上 | 新资金记账逐科目明细；生产为 100 分片表族 |
| ease-pay-bill | `order_message_record` | 测试缓存：`159.138.165.0:2883 / ep_bill`<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_bill` | Bill 本地业务消息发送记录 |
| ease-pay-bill | `mq_exception_record` | 测试：DataGrip 旧缓存未见；当前代码已使用<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_bill` | Bill MQ 异常和补偿检查点 |
| ease-pay-trade | `tra_bnpl_mq_exception` | 测试缓存：`159.138.165.0:2883 / ep_pay_trade`<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_pay_trade` | Trade MQ 异常和补偿检查点 |
| ease-pay-channel | `channel_mq_exception` | 测试缓存：`159.138.165.0:2883 / ep_pay_channel`<br>生产在线：`10.226.10.147-ng_loan_collect-ob / bnpl_ep_pay_channel` | Channel MQ 异常和补偿检查点 |

```text
bnpl_ep_pay_trade.tra_repayment_order.trans_no
-> bnpl_ep_pay_channel.pay_channel_in.bus_order_id
= bnpl_ep_bill.bill_repay_transaction.repay_no
= bnpl_ep_bill.user_bill_detail.trans_no
```

生产库使用 DBS 租户前缀 `bnpl_`，代码和 DataGrip 使用 `ep_*`。这是已验证的环境别名，不应通过字符串相似性外推其他实例。

DBS 当前账号可看到测试实例 `10.220.4.60-ng_bnpl_test-ob`，但数据库下拉为空，因此只记为 `实例-only`，不能写成已在线确认 `ep_bill/ep_pay_trade/ep_pay_channel`。

## 核验结论

1. OK、EM、ML 传统还款的表职责基本同构，但实例和物理库分别隔离，不能跨实例 JOIN。
2. ML OD、BNPL 都是 Trade/Channel/Bill 模型，但表名、字段和账单分配模型不同，不能相互套 SQL。
3. 生产表均以 2026-08-31 DBS 只读核验为准；测试实例在 DBS 中可见，但当前账号无法展开库表，测试库名主要来自 DataGrip 缓存与代码配置。
4. BNPL `bill_repay_trans_detail` 是当前在线表结构唯一未见的代码表；它属于旧写入/迁移兼容模型。当前线上查单使用 `user_bill_detail`，并结合 `bill_post_detail`、`user_bill_out_account` 核对金额分配，多还余额优先查 `bill_account_balance*`。
5. `payment/user_e_trans`、Bill paid 累计、Trade 支付成功、Channel 成功分别证明不同层级，任何单层成功都不能直接等同全链还款完成。

## 证据来源

- `OPAY-线上线下库表关系梳理.md`
- `OPAY-线上查单手册.md`
- `/Users/guozhixiang/Code/Loan System Docs/CashLoan/03-还款/`
- `/Users/guozhixiang/Code/Loan System Docs/ML/传统借据/03-还款/`
- `/Users/guozhixiang/Code/Loan System Docs/ML/OD循环额度/03-还款/`
- `/Users/guozhixiang/Code/Loan System Docs/BNPL/03-还款/`
- DBS 已登录生产只读页面与 DataGrip 本地缓存
