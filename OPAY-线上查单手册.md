# OPAY 线上查单手册

> 核验时间：2026-08-31（Asia/Shanghai）  
> 适用范围：EM（Easemoni）、OK（Okash）、ML（Merchant Loan，含传统借据与 OD）、BNPL 的放款/出金、用信和还款查单。  
> 证据口径：当前代码工作树 + DBS 已登录生产只读页面。SQL 模板均按真实线上实例/库拆分；2026-08-31 已用 `WHERE 1=0` 或零命中业务键完成关键表与关联字段语法核验。

## 0. 使用方式

### 0.1 先判断业务线和模型

| 业务线 | 模型 | 放款/用信入口键 | 还款入口键 | 线上实例与库 |
|---|---|---|---|---|
| EM | 传统现金贷 | `applyId / apply_no` | `repayment_order_no` | Service：`10.224.10.17-ng-em-service-ob-slave/{okash,pay_channel}`；Core：`10.224.10.17-ng-em-core-ob-slave/microloan` |
| OK | 传统现金贷 | `applyId / apply_no` | `repayment_order_no` | `10.66.1.139-ng-okash-ob/{okash,microloan,pay_channel}` |
| ML | 传统固定期限借据 | `applyId / orders.order_id` | `repayment_order_no` | Service：`10.222.6.88-ease-merchant-loan-service-slave/{ml_ease,ml_channel}`；Core：`10.222.6.130-ease-merchant-loan-core-slave/ml_microloan` |
| ML OD | 循环额度 | `trans_no` 或 `third_pay_no` | OD `tra_repayment_order.trans_no` | Service：`10.222.6.88-ease-merchant-loan-service-slave/ml_od_trade`；Bill：`10.222.6.130-ease-merchant-loan-core-slave/ml_bill`；Channel：Service 实例的 `ml_channel` |
| BNPL | 消费/分期/提现 | `trans_no`、`third_pay_no` 或 `withdrawal_trans_no` | BNPL `tra_repayment_order.trans_no` | `10.226.10.147-ng_loan_collect-ob/{bnpl_ep_pay_trade,bnpl_ep_pay_channel,bnpl_ep_bill}` |

命名归一：`okask` = OK，`easemoni` = EM，`merchant` = ML。

### 0.2 工单至少收集这些键

| 必填项 | 说明 |
|---|---|
| 业务线 + 模型 | EM、OK、ML 传统、ML OD、BNPL；先分清模型再查表 |
| 问题类型 | 放款/出金、用信、还款、退款或对账 |
| 主业务号 | `applyId`、`repayment_order_no`、`trans_no` 中至少一个 |
| 用户号 | ML OD、BNPL 和 Channel 分片查询强烈建议同时提供 `user_id` |
| 外部号 | 银行/钱包/商户给出的 `thirdPayNo`、`trans_reference`、渠道 `order_id` |
| 时间窗 | 业务发生时间及统一时区；跨服务排序不能只看本地时间字段名 |

### 0.3 执行规则

1. 只执行 `SELECT`；每条 SQL 必须带精确业务键和 `LIMIT`。
2. 将 `<...>` 整体替换为真实单号；不要原样执行占位符。
3. 不跨 DBS 实例直接 `JOIN`。先在上游表取下一跳单号，再切换实例/库查询。
4. `SELECT *` 是有意保留：线上非关键展示列存在版本漂移，查单主链只依赖 `WHERE` 中已经核验的业务键。
5. 查询结果中的手机号、银行卡、证件、姓名等敏感字段不得直接贴到群或工单；仅保留必要单号、状态、金额和脱敏结果。
6. 一层成功不代表全链成功。固定按“业务/Core → Trade → Channel → 三方 → 回调 → 账务”逐层确认。

# 1. EM / OK 现金贷

## 1.1 服务、表与库

| 服务 | 表 | 库 | 查单作用 |
|---|---|---|---|
| Service / Pay Trade | `okash_loan_apply`, `okash_loan_apply_mark`, `pay_b2c` | `okash` | 申请主单、申请标记、放款编排单 |
| Core | `orders`, `orders_fee_float`, `repay_plan` | `microloan` | 借据、费率快照、应还计划 |
| Pay Channel | `pay_channel_out`, `pay_channel_out_ext`, `pay_channel_out_send_log`, `pay_channel_out_unusual_trans` | `pay_channel` | 渠道出金主单、扩展、每次发送和异常处理 |
| Pay Trade | `pay_repayment_order`, `pay_repayment_core_detail` | `okash` | 还款支付总单与分借据 Core 入账明细 |
| Pay Channel | `pay_channel_in` | `pay_channel` | 渠道入金单、三方流水与失败原因 |
| Core | `payment`, `user_e_trans`, `repay_plan`, `orders` | `microloan` | Core 入账聚合、科目流水、计划和借据终态 |

## 1.2 放款单号关系

CashLoan 的 Core 业务主键是 `orders.id`，不是 ML 传统借据的 `orders.order_id`：

```text
okash_loan_apply.apply_no
  = okash_loan_apply_mark.loan_apply_no
  = microloan.orders.id
  = microloan.orders_fee_float.order_id
  = microloan.repay_plan.order_id
  = okash.pay_b2c.apply_id
  = okash.pay_b2c.order_ref

okash.pay_b2c.b2c_ref
  = pay_channel.pay_channel_out.bus_order_id
  = pay_channel.pay_channel_out_ext.bus_order_id
  = pay_channel.pay_channel_out_send_log.bus_order_id
  = pay_channel.pay_channel_out_unusual_trans.bus_order_id
```

### 第一步：查申请与 Trade 放款单

DBS 选择：EM 用 `10.224.10.17-ng-em-service-ob-slave / okash`；OK 用 `10.66.1.139-ng-okash-ob / okash`。

```sql
SELECT *
FROM okash_loan_apply
WHERE apply_no = '<APPLY_ID>'
LIMIT 20;
```

```sql
SELECT *
FROM okash_loan_apply_mark
WHERE loan_apply_no = '<APPLY_ID>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM pay_b2c
WHERE apply_id = '<APPLY_ID>'
ORDER BY id DESC
LIMIT 20;
```

如果只拿到渠道业务号：

```sql
SELECT *
FROM pay_b2c
WHERE b2c_ref = '<B2C_REF>'
ORDER BY id DESC
LIMIT 20;
```

重点看：申请 `status/is_cleared`；Trade `b2c_ref/pay_result/fail_reason/amount`。先从 `pay_b2c.b2c_ref` 取下一跳。

### 第二步：查 Core 借据和计划

DBS 选择：EM 用 `10.224.10.17-ng-em-core-ob-slave / microloan`；OK 用 `10.66.1.139-ng-okash-ob / microloan`。

```sql
SELECT *
FROM orders
WHERE id = '<APPLY_ID>'
LIMIT 20;
```

```sql
SELECT *
FROM orders_fee_float
WHERE order_id = '<APPLY_ID>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM repay_plan
WHERE order_id = '<APPLY_ID>'
ORDER BY current_stage_id, id
LIMIT 100;
```

Core `check_status` 主链：`5` 等待放款、`6` 放款失败、`7` 等待还款、`8` 已结清、`9` 逾期、`15` 展期结清。放款完成至少应看到 Core 从 `5` 离开，并按产品生成 `repay_plan`。

### 第三步：查 Channel 出金过程

DBS 选择：EM 用 `10.224.10.17-ng-em-service-ob-slave / pay_channel`；OK 用 `10.66.1.139-ng-okash-ob / pay_channel`。

```sql
SELECT *
FROM pay_channel_out
WHERE bus_order_id = '<B2C_REF>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM pay_channel_out_ext
WHERE bus_order_id = '<B2C_REF>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM pay_channel_out_send_log
WHERE bus_order_id = '<B2C_REF>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM pay_channel_out_unusual_trans
WHERE bus_order_id = '<B2C_REF>'
ORDER BY id
LIMIT 100;
```

重点看：Channel `result/order_id/trans_reference/fail_reason`；发送日志 `status/order_msg/retry_flag/send_time`；异常表 `unusual_trans_status/handle_flag/handle_retry_order_id`。

### 放款断点判断

| 现象 | 结论范围 | 下一步 |
|---|---|---|
| `orders` 有单，`pay_b2c` 无单 | Core 已创单，不证明 Trade 收到放款消息 | 查 Core `order_message_record` 和 MQ 发送/消费日志 |
| `pay_b2c=QUEUED`，Channel 无单 | Trade 已创单，Channel 请求未闭环 | 查 Trade 编排、Topic 和 Channel 消费日志 |
| `pay_channel_out=SUCCESS`，`pay_b2c` 未成功 | 渠道已成功，Trade 回调/消费未闭环 | 查 Channel 通知与 Trade callback |
| `pay_b2c=SUCCESS`，Core 仍 `check_status=5` | Trade 已成功，Core 放款结果未建账 | 查 Trade → Core 回调、Core `/api/disburse` 与补偿任务 |
| Core 为 `7` 但无计划 | 借据状态与计划事实不一致 | 核对 Core 放款事务和部署版本，不能仅按状态关单 |

## 1.3 还款单号关系

```text
okash.pay_repayment_order.repayment_order_no
  = pay_channel.pay_channel_in.bus_order_id
  = okash.pay_repayment_core_detail.repayment_order_no

okash.pay_repayment_core_detail.apply_id
  = microloan.orders.id
  = microloan.repay_plan.order_id

okash.pay_repayment_core_detail.repayment_core_no
  = microloan.payment.batch_no
  = microloan.user_e_trans.batch_no

microloan.payment.id
  = microloan.user_e_trans.payment_id
```

### 第一步：查 Trade 还款总单与 Core 明细

DBS 选择：EM/OK 的 `okash`，实例同 1.2 第一步。

```sql
SELECT *
FROM pay_repayment_order
WHERE repayment_order_no = '<REPAYMENT_ORDER_NO>'
LIMIT 20;
```

```sql
SELECT *
FROM pay_repayment_core_detail
WHERE repayment_order_no = '<REPAYMENT_ORDER_NO>'
ORDER BY id
LIMIT 100;
```

总单 `repayment_order_sts`：`-1` 已创单、`0` 支付中、`1` 支付成功、`2` 支付失败、`3` Core 记账成功、`4` 冲正。明细 `repayment_core_sts`：`0` 初始、`1` 成功、`2` 失败、`3` 冲正。

### 第二步：查 Channel 入金

DBS 选择：EM/OK 的 `pay_channel`，实例同 1.2 第三步。

```sql
SELECT *
FROM pay_channel_in
WHERE bus_order_id = '<REPAYMENT_ORDER_NO>'
ORDER BY id DESC
LIMIT 20;
```

重点看 `result/order_id/trans_reference/amount/fail_reason/finish_time`。`order_id` 和 `trans_reference` 是 Channel/三方号，不等于 Core `batch_no`。

### 第三步：逐个 Core 明细查入账事实

从每条 `pay_repayment_core_detail` 取 `apply_id` 与 `repayment_core_no`。DBS 选择 EM/OK 的 `microloan`。

```sql
SELECT *
FROM orders
WHERE id = '<APPLY_ID>'
LIMIT 20;
```

```sql
SELECT *
FROM payment
WHERE batch_no = '<REPAYMENT_CORE_NO>'
   OR order_id = '<APPLY_ID>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM user_e_trans
WHERE batch_no = '<REPAYMENT_CORE_NO>'
   OR order_id = '<APPLY_ID>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM repay_plan
WHERE order_id = '<APPLY_ID>'
ORDER BY current_stage_id, id
LIMIT 100;
```

### 还款断点判断

| 现象 | 结论范围 | 下一步 |
|---|---|---|
| Channel 成功，总单仍 `0` | 收款完成，Trade 回调未闭环 | 查 Channel 通知和 Trade callback |
| 总单 `1`，没有 Core detail | 支付成功，尚未完成分借据入账编排 | 查拆分任务、`apply_id_json` 和 Trade 告警 |
| detail `2` | 对应借据 Core 入账失败 | 用 `repayment_core_no` 查 Core 日志和历史入账 |
| detail `1`，Core 无 `payment/user_e_trans` | Trade 与 Core 事实不一致；也可能是“借据已结清、零入账”分支 | 结合 `core_amount`、Core 返回码和订单状态判断 |
| Core 有入账，总单未到 `3` | 明细汇总或回写未闭环 | 查是否还有其他 detail 未成功及总单更新日志 |

# 2. ML Merchant Loan

## 2.1 传统借据与 EM/OK 的关键差异

ML 传统借据的业务申请号在 `orders.order_id`；`orders.id` 是内部数值主键，`repay_plan/payment/user_e_trans.order_id` 关联的是内部 `orders.id`：

```text
ml_ease.okash_loan_apply.apply_no
  = ml_microloan.orders.order_id
  = ml_ease.pay_b2c.apply_id

ml_microloan.orders.id
  = ml_microloan.repay_plan.order_id
  = ml_microloan.payment.order_id
  = ml_microloan.user_e_trans.order_id

ml_ease.pay_b2c.b2c_ref
  = ml_channel.pay_channel_out.bus_order_id
```

不能把 ML `orders.order_id` 查询复制到 EM/OK；也不能用 ML `orders.id` 去查 `pay_b2c.apply_id`。

## 2.2 传统借据放款

### 第一步：Service / Trade

DBS 选择：`10.222.6.88-ease-merchant-loan-service-slave / ml_ease`。

```sql
SELECT *
FROM okash_loan_apply
WHERE apply_no = '<APPLY_ID>'
LIMIT 20;
```

```sql
SELECT *
FROM okash_loan_apply_mark
WHERE loan_apply_no = '<APPLY_ID>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM pay_b2c
WHERE apply_id = '<APPLY_ID>'
ORDER BY id DESC
LIMIT 20;
```

### 第二步：Core

DBS 选择：`10.222.6.130-ease-merchant-loan-core-slave / ml_microloan`。

```sql
SELECT *
FROM orders
WHERE order_id = '<APPLY_ID>'
LIMIT 20;
```

先从结果取内部 `orders.id`：

```sql
SELECT *
FROM repay_plan
WHERE order_id = '<ORDERS_INTERNAL_ID>'
ORDER BY current_stage_id, id
LIMIT 100;
```

ML `check_status` 主链：`5` 待放款、`6` 放款失败、`7` 待还款、`8` 结清、`9` 逾期、`11` 部分还款。

### 第三步：Channel

DBS 选择：`10.222.6.88-ease-merchant-loan-service-slave / ml_channel`。

```sql
SELECT *
FROM pay_channel_out
WHERE bus_order_id = '<B2C_REF>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM pay_channel_out_send_log
WHERE bus_order_id = '<B2C_REF>'
ORDER BY id
LIMIT 100;
```

断点判断与 EM/OK 相同，但库必须限定为 `ml_ease/ml_channel/ml_microloan`。

## 2.3 传统借据还款

```text
ml_ease.pay_repayment_order.repayment_order_no
  = ml_channel.pay_channel_in.bus_order_id
  = ml_ease.pay_repayment_core_detail.repayment_order_no

ml_ease.pay_repayment_core_detail.apply_id
  = ml_microloan.orders.order_id

ml_ease.pay_repayment_core_detail.repayment_core_no
  = ml_microloan.payment.batch_no
  = ml_microloan.user_e_trans.batch_no
```

在 `ml_ease`：

```sql
SELECT *
FROM pay_repayment_order
WHERE repayment_order_no = '<REPAYMENT_ORDER_NO>'
LIMIT 20;
```

```sql
SELECT *
FROM pay_repayment_core_detail
WHERE repayment_order_no = '<REPAYMENT_ORDER_NO>'
   OR apply_id = '<APPLY_ID>'
ORDER BY id
LIMIT 100;
```

在 `ml_channel`：

```sql
SELECT *
FROM pay_channel_in
WHERE bus_order_id = '<REPAYMENT_ORDER_NO>'
ORDER BY id DESC
LIMIT 20;
```

在 `ml_microloan`，先查内部主键：

```sql
SELECT *
FROM orders
WHERE order_id = '<APPLY_ID>'
LIMIT 20;
```

```sql
SELECT *
FROM payment
WHERE batch_no = '<REPAYMENT_CORE_NO>'
   OR order_id = '<ORDERS_INTERNAL_ID>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM user_e_trans
WHERE batch_no = '<REPAYMENT_CORE_NO>'
   OR order_id = '<ORDERS_INTERNAL_ID>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM repay_plan
WHERE order_id = '<ORDERS_INTERNAL_ID>'
ORDER BY current_stage_id, id
LIMIT 100;
```

总单和 detail 状态值与 EM/OK 相同。`repayment_order_sts=1` 只代表支付成功；至少继续检查 detail 和 Core 入账事实。

## 2.4 ML OD 用信/消费

OD 不是传统 `orders + repay_plan` 放款模型：

```text
third_order_no / third_pay_no
  -> ml_od_trade.tra_transfer_transaction.trans_no
  -> ml_bill.bill_transfer_transaction.transfer_trans_no
  -> ml_bill.bill_transfer_transaction.post_no
  -> ml_bill.bill_post_plan.post_no
  -> ml_bill.bill_statement_trans.trans_no
```

在 `10.222.6.88-ease-merchant-loan-service-slave / ml_od_trade`：

```sql
SELECT *
FROM tra_transfer_transaction
WHERE trans_no = '<TRANS_NO>'
   OR third_pay_no = '<THIRD_PAY_NO>'
   OR third_order_no = '<THIRD_ORDER_NO>'
ORDER BY id DESC
LIMIT 20;
```

重点看 `trans_status/bill_flag/confirm_action_no`。`trans_status=1` 只说明 OD Trade 成功；`bill_flag=1` 才表示 Bill 已回执。

在 `10.222.6.130-ease-merchant-loan-core-slave / ml_bill`：

```sql
SELECT *
FROM bill_transfer_transaction
WHERE transfer_trans_no = '<TRANS_NO>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM bill_post_plan
WHERE post_no = '<POST_NO>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM bill_statement_trans
WHERE trans_no = '<TRANS_NO>'
   OR third_trans_no = '<THIRD_PAY_NO>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM bill_statement_info
WHERE stat_no = '<STAT_NO>'
   OR user_id = '<USER_ID>'
ORDER BY id DESC
LIMIT 100;
```

## 2.5 ML OD 还款

```text
ml_od_trade.tra_repayment_order.trans_no
  = ml_channel.pay_channel_in.bus_order_id
  = ml_bill.bill_repay_transaction.repay_trans_no
  = ml_bill.bill_statement_trans.trans_no
```

在 `ml_od_trade`：

```sql
SELECT *
FROM tra_repayment_order
WHERE trans_no = '<OD_REPAY_TRANS_NO>'
   OR third_trans_no = '<THIRD_REF>'
   OR release_action_no = '<ACTION_NO>'
ORDER BY id DESC
LIMIT 20;
```

重点看 `repayment_order_sts/bill_flag/pay_time/release_action_no`。支付成功但 `bill_flag=0` 是 Trade 成功、Bill 未闭环。

在 `ml_channel`：

```sql
SELECT *
FROM pay_channel_in
WHERE bus_order_id = '<OD_REPAY_TRANS_NO>'
ORDER BY id DESC
LIMIT 20;
```

同时确认 `biz_type=OD`，避免落入传统 ML 默认分支。

在 `ml_bill`：

```sql
SELECT *
FROM bill_repay_transaction
WHERE repay_trans_no = '<OD_REPAY_TRANS_NO>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM bill_statement_trans
WHERE trans_no = '<OD_REPAY_TRANS_NO>'
   OR third_trans_no = '<THIRD_REF>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM bill_statement_info
WHERE stat_no = '<STAT_NO>'
ORDER BY id DESC
LIMIT 20;
```

# 3. BNPL

## 3.1 不套用传统放款模型

BNPL 没有传统 `orders → pay_b2c → repay_plan`。需要区分：

1. 消费/分期用信：`tra_consume_transaction` / `tra_installment_transaction`。
2. 额度动作：`tra_op_freeze_detail`、`tra_op_confirm_detail`、`tra_op_unfreeze_detail`、`tra_op_release_detail`。
3. CashBack 提现/出金：`tra_withdrawal_transaction → pay_channel_opay_cash_back`。
4. 还款：`tra_repayment_order → pay_channel_in → bill_repay_transaction → user_bill_detail`。

所有查询使用实例 `10.226.10.147-ng_loan_collect-ob`，再按库切换。

## 3.2 消费/分期用信

```text
third_order_no / third_pay_no
  -> bnpl_ep_pay_trade.tra_consume_transaction.trans_no
  -> bnpl_ep_pay_trade.tra_op_*_detail.trans_no
  -> bnpl_ep_bill.bill_consume_transaction.trans_no
  -> bnpl_ep_bill.user_bill_detail.trans_no
  -> user_bill_detail.bill_no
  -> bnpl_ep_bill.user_bill_out_account.bill_no
```

在 `bnpl_ep_pay_trade`：

```sql
SELECT *
FROM tra_consume_transaction
WHERE trans_no = '<TRANS_NO>'
   OR third_pay_no = '<THIRD_PAY_NO>'
   OR third_order_no = '<THIRD_ORDER_NO>'
ORDER BY id DESC
LIMIT 20;
```

分期单：

```sql
SELECT *
FROM tra_installment_transaction
WHERE trans_no = '<TRANS_NO>'
ORDER BY id DESC
LIMIT 20;
```

额度动作分别查询，不要把四张表强行 UNION：

```sql
SELECT * FROM tra_op_freeze_detail
WHERE trans_no = '<TRANS_NO>' ORDER BY id LIMIT 20;
```

```sql
SELECT * FROM tra_op_confirm_detail
WHERE trans_no = '<TRANS_NO>' ORDER BY id LIMIT 20;
```

```sql
SELECT * FROM tra_op_unfreeze_detail
WHERE trans_no = '<TRANS_NO>' ORDER BY id LIMIT 20;
```

```sql
SELECT * FROM tra_op_release_detail
WHERE trans_no = '<TRANS_NO>' ORDER BY id LIMIT 20;
```

Trade `trans_status`：`-1` 初始化、`0` 处理中、`1` 成功、`2` 失败、`3` 关单；`bill_flag=0/1` 表示 Bill 未/已完成。

在 `bnpl_ep_bill`：

```sql
SELECT *
FROM bill_consume_transaction
WHERE trans_no = '<TRANS_NO>'
ORDER BY id DESC
LIMIT 20;
```

```sql
SELECT *
FROM user_bill_detail
WHERE trans_no = '<TRANS_NO>'
ORDER BY id
LIMIT 100;
```

```sql
SELECT *
FROM user_bill_out_account
WHERE bill_no = '<BILL_NO>'
ORDER BY id DESC
LIMIT 20;
```

## 3.3 CashBack 提现/出金

```text
bnpl_ep_pay_trade.tra_withdrawal_transaction.withdrawal_trans_no
  = Channel cashBackNo
  = bnpl_ep_pay_channel.pay_channel_opay_cash_back.bus_order_id
```

在 `bnpl_ep_pay_trade`：

```sql
SELECT *
FROM tra_withdrawal_transaction
WHERE withdrawal_trans_no = '<WITHDRAWAL_TRANS_NO>'
   OR biz_no = '<BIZ_NO>'
ORDER BY id DESC
LIMIT 20;
```

`withdrawal_status`：`0` 处理中、`1` 成功、`2` 失败。

在 `bnpl_ep_pay_channel`：

```sql
SELECT *
FROM pay_channel_opay_cash_back
WHERE bus_order_id = '<WITHDRAWAL_TRANS_NO>'
ORDER BY id DESC
LIMIT 20;
```

重点看 `order_id/result/trans_reference/fail_reason`。这里的 `pay_channel_out` 是通用出金表，当前 CashBack 主链以专用 `pay_channel_opay_cash_back` 为准。

## 3.4 BNPL 还款

```text
bnpl_ep_pay_trade.tra_repayment_order.trans_no
  -> bnpl_ep_pay_channel.pay_channel_in.bus_order_id（可能附加用户分片尾号）
  = bnpl_ep_bill.bill_repay_transaction.repay_no
  = bnpl_ep_bill.user_bill_detail.trans_no
```

在 `bnpl_ep_pay_trade`：

```sql
SELECT *
FROM tra_repayment_order
WHERE trans_no = '<BNPL_REPAY_NO>'
ORDER BY id DESC
LIMIT 20;
```

状态：`repayment_order_sts=-1/0/1/2/4` 分别为已下单、支付中、支付成功、支付失败、冲正；`bill_flag=0/1` 表示 Bill 未/已完成。

在 `bnpl_ep_pay_channel`，精确查询需要同时拿 `user_id`：

```sql
SELECT *
FROM pay_channel_in
WHERE user_id = '<USER_ID>'
  AND bus_order_id LIKE CONCAT('<BNPL_REPAY_NO>', '%')
ORDER BY id DESC
LIMIT 20;
```

在 `bnpl_ep_bill`：

```sql
SELECT *
FROM bill_repay_transaction
WHERE repay_no = '<BNPL_REPAY_NO>'
ORDER BY id DESC
LIMIT 20;
```

当前生产可用的分账明细入口：

```sql
SELECT *
FROM user_bill_detail
WHERE trans_no = '<BNPL_REPAY_NO>'
ORDER BY id
LIMIT 100;
```

代码还会写 `bill_repay_trans_detail`，但 2026-08-31 DBS 线上 `bnpl_ep_bill` 执行查询返回表不存在；当前查单不得用其他同名表替代。该表部署后再启用以下模板：

```sql
-- 当前线上不可执行：等待 bill_repay_trans_detail 部署确认
SELECT *
FROM bill_repay_trans_detail
WHERE trans_no = '<BNPL_REPAY_NO>'
ORDER BY id
LIMIT 100;
```

### BNPL 断点判断

| 现象 | 结论范围 | 下一步 |
|---|---|---|
| Trade `repayment_order_sts=1`，`bill_flag=0` | 支付成功，Bill 未闭环 | 查 Bill MQ、`bill_repay_transaction` 和 `user_bill_detail` |
| Channel 成功，Trade 仍支付中 | Channel → Trade 回调未闭环 | 查 callback 时间线和 Trade handler |
| `bill_repay_transaction` 已入账，Trade `bill_flag=0` | Bill → Trade 完成通知未闭环 | 查 Bill 完成事件及 Trade listener |
| Bill 主单已入账，`user_bill_detail` 不完整 | Bill 本地存在半完成窗口 | 先做金额/账单明细对账，禁止盲目重推 |
| 自动代扣异常 | 先区分正常/逾期批次 | 核对账单状态、来源类型、Trade 单、Channel 单和 Bill 入账 |

## 4. 快速状态阶梯

| 业务模型 | 第一层 | 第二层 | 第三层 | 完成判断 |
|---|---|---|---|---|
| EM/OK 放款 | Core `orders` | Trade `pay_b2c` | Channel `pay_channel_out/send_log` | Channel、Trade、Core 三层均成功且 Core 已有计划 |
| EM/OK 还款 | Channel `pay_channel_in` | Trade 总单/detail | Core `payment/user_e_trans/repay_plan` | 总单 `3` + 明细完成 + Core 有入账事实 |
| ML 传统放款 | Core `orders.order_id` | `ml_ease.pay_b2c` | `ml_channel.pay_channel_out` | 同时核对 Core 内部 `orders.id` 下的计划 |
| ML 传统还款 | `ml_channel.pay_channel_in` | `ml_ease.pay_repayment_*` | `ml_microloan` 入账表 | 不能把业务 `order_id` 与内部 `orders.id` 混用 |
| ML OD 用信 | OD Trade `trans_status` | Bill transfer/post plan | Statement | `trans_status=1` 且 `bill_flag=1`，Bill 明细存在 |
| ML OD 还款 | Channel / OD Trade | Bill repay | Statement | Trade 支付成功且 `bill_flag=1`，Bill/Statement 明细存在 |
| BNPL 消费 | Trade + Quota 动作 | Bill consume/detail | Bill out account | Trade 成功、额度动作成功、`bill_flag=1`、Bill 明细存在 |
| BNPL CashBack | Trade withdrawal | Channel CashBack | 三方流水 | Trade 与 Channel 终态一致 |
| BNPL 还款 | Channel / Trade | Bill repay | `user_bill_detail` | Trade 支付成功、`bill_flag=1`、Bill 主单和明细存在 |

## 5. 线上可执行性核验

以下校验均在 DBS 生产只读页面执行，只使用 `SELECT ... WHERE 1=0` 或零命中占位业务键，不读取业务数据：

| 业务线 | 实例 / 库 | 核验范围 | 结果 |
|---|---|---|---|
| EM | Service / `okash` | 申请、标记、`pay_b2c`、还款总单/detail 关联键 | 通过 |
| EM | Core / `microloan` | `orders`、费率、计划、Payment、UserETrans 关联键 | 通过 |
| EM | Service / `pay_channel` | 出入金、扩展、发送日志、异常交易 `bus_order_id` | 通过 |
| OK | `okash` | 申请、标记、Trade 放款/还款 | 通过 |
| OK | `microloan` | Core 借据、计划和入账 | 通过 |
| OK | `pay_channel` | 出入金、扩展、发送日志和异常交易 | 通过 |
| ML 传统 | `ml_ease` | `pay_b2c`、还款总单/detail | 通过 |
| ML 传统 | `ml_microloan` | 业务 `order_id`、内部 `id`、计划和入账 | 通过 |
| ML Channel | `ml_channel` | 出入金和 Channel 过程键 | 通过 |
| ML OD | `ml_od_trade` | 消费/用信和 OD 还款单号 | 通过 |
| ML OD | `ml_bill` | Transfer、PostPlan、Statement、Repay 关联键 | 通过 |
| BNPL | `bnpl_ep_pay_trade` | 消费、分期、退款、还款、提现和额度动作 | 通过 |
| BNPL | `bnpl_ep_pay_channel` | PayIn 与 CashBack 业务键 | 通过 |
| BNPL | `bnpl_ep_bill` | 消费、退款、还款主单、账单与 `user_bill_detail` | 通过 |
| BNPL 缺口 | `bnpl_ep_bill.bill_repay_trans_detail` | 表存在性 | 未通过：线上返回表不存在；已从当前主查单路径移除 |

初版曾显式选择 `okash_loan_apply.update_time`，线上返回列不存在。最终模板统一改为 `SELECT * + 已核验业务键 + LIMIT`，避免非关键展示列漂移阻断查单。

## 6. 代码证据索引

| 链路 | 关键代码证据 |
|---|---|
| EM/OK 放款 `apply_id → b2c_ref` | `/Users/guozhixiang/Code/CashLoan/pay-trade/pay-trade-server/src/main/java/com/blueridge/pay/trade/service/payout/DisburseOutHandlerServiceImpl.java` |
| EM/OK Channel 出金键 | `/Users/guozhixiang/Code/CashLoan/pay-channel-ng/pay-channel-server/src/main/resources/mapper/PayChannelOutMapper.xml`、`PayChannelOutSendLogMapper.xml` |
| EM/OK 还款总单/detail/Core | `/Users/guozhixiang/Code/CashLoan/pay-trade/pay-trade-server/src/main/resources/mapper/PayRepaymentOrderMapper.xml`、`PayRepaymentCoreDetailMapper.xml`；`/Users/guozhixiang/Code/CashLoan/core-ng/core-common-module/src/main/java/com/opera/okash/core/modular/wrapper/PaymentWrapper.java` |
| ML 传统放款 | `/Users/guozhixiang/Code/ML/pay-trade/pay-trade-server/src/main/java/com/blueridge/ml/pay/trade/service/impl/core/notify/CorePayOutNotifyHandlerServiceImpl.java`；`/Users/guozhixiang/Code/ML/core/ml-core-common-module/src/main/java/com/br/merchant/core/modular/mq/ProducerService.java` |
| ML 传统还款 | `/Users/guozhixiang/Code/ML/pay-trade/pay-trade-server/src/main/java/com/blueridge/ml/pay/trade/domain/CashierDomainService.java`；`/Users/guozhixiang/Code/ML/pay-trade/pay-trade-server/src/main/resources/mapper/PayRepaymentCoreDetailMapper.xml` |
| ML OD 用信/还款 | `/Users/guozhixiang/Code/ML/ML_OD/ml-od-trade/ml-od-trade-server/src/main/resources/mapper/TraTransferTransactionMapper.xml`、`TraRepaymentOrderMapper.xml`；`/Users/guozhixiang/Code/ML/ML_OD/ml-bill/ml-bill-common-module/src/main/resources/mapper/` |
| BNPL 消费与额度 | `/Users/guozhixiang/Code/BNPL/ease-pay-trade/pay-trade-web/src/main/java/com/ease/pay/trade/domain/TransactionDomainService.java`、`/Users/guozhixiang/Code/BNPL/ease-pay-trade/pay-trade-web/src/main/java/com/ease/pay/quota/domain/QuotaDomainService.java` |
| BNPL CashBack | `/Users/guozhixiang/Code/BNPL/ease-pay-trade/pay-trade-web/src/main/java/com/ease/pay/trade/domain/WithdrawalDomainService.java`；`/Users/guozhixiang/Code/BNPL/ease-pay-channel/pay-channel-web/src/main/resources/mapper/PayChannelOpayCashBackMapper.xml` |
| BNPL 还款 | `/Users/guozhixiang/Code/BNPL/ease-pay-trade/pay-trade-web/src/main/java/com/ease/pay/trade/domain/CashierDomainService.java`；`/Users/guozhixiang/Code/BNPL/ease-pay-bill/ease-pay-bill-server/src/main/java/com/ease/pay/bill/module/repay/domain/RepayTransPostDomainService.java` |

资产与表覆盖基线见同目录 [`OPAY-线上线下库表关系梳理.md`](./OPAY-线上线下库表关系梳理.md)。
