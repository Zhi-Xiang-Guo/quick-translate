# OPAY 线上 / 线下库表关系梳理

> 盘点时间：2026-08-31（Asia/Shanghai）  
> 盘点范围：`/Users/guozhixiang/Code` 下 17 个业务 Git 仓库、DataGrip 项目 `Opay` 的 4 个数据源缓存、DBS 在线页面与用户提供的截图。  
> 展示口径：业务线为一级标题；线上采用“实例 + 库 + 表/表族”；线下采用“DataGrip 数据源 + 库 + 表/表族”。

## 0. 证据口径与结论

### 0.1 证据等级

| 标记 | 含义 |
|---|---|
| `运行缓存` | DataGrip 已缓存该 schema/table；证明快照时可见，不等于当前实时状态 |
| `代码配置` | 当前工作树存在 JDBC/datasource/环境覆盖配置；不能单独证明当前生产部署 |
| `代码访问` | 当前工作树存在 Mapper、SQL 或活动 ORM 调用；证明代码涉及该逻辑表 |
| `截图可见` | 用户截图的线上实例下拉框可见；未进入实例读取实时 schema |
| `在线实时` | 2026-08-31 通过已登录 DBS 页面只读展开实例、数据库和表结构下拉；未执行 SQL |
| `权限记录` | DBS 权限管理页中处于 Approved 状态的实例/数据库授权记录 |
| `候选` | 只有 ORM/DDL/命名线索，缺少活动调用或运行库证据 |

### 0.2 总量

| 业务线 | 仓库范围 | 代码侧仓库-表条目 | 运行缓存摘要 |
|---|---|---:|---|
| 现金贷（Easemoni / Okash） | `EM/service-ng` + `CashLoan/*` | 173 次活动引用 / 159 个线内唯一活动表；另 4 个实体候选 | EM 连接 720 张缓存表；OK 连接 686 张缓存表 |
| 商户贷（ML，含传统借据与 OD） | `ML/*` | 195 次活动引用 / 173 个线内唯一活动表；另 7 个模型候选 | ML 连接 435 张缓存表 |
| BNPL | `BNPL/*` | 47 次活动引用 / 47 个线内唯一活动表；另 1 个 DDL-only 候选 | BNPL 连接 3,664 张缓存表；核心三库 2,573 张 |
| 合计 | 17 个业务仓库 | 415 次活动引用 / 247 个全局唯一活动表；另 12 次候选引用 | 4 个 DataGrip 数据源共 5,505 张缓存表 |

说明：同一逻辑表被多个仓库访问时会形成多次“仓库-表引用”；全局去重后是 247 个活动逻辑表。12 次候选引用对应 9 个唯一候选名，其中 `okash_virtual_random`, `pay_channel_in_batch`, `pay_channel_in_ext_batch` 已被 CashLoan 仓库活动使用，所以全局真正“只有候选证据”的名称为 6 个。物理分表不重复计入逻辑表总数；Redis key、MQ topic/tag、Apollo/Nacos namespace 不计为关系表。

DBS 当前实例下拉共 53 个资产：MySQL 23 个、OceanBase 26 个、Redis 2 个、MongoDB 2 个；其中 49 个关系型实例逐项收入附录，4 个非关系型实例单列为排除项。

### 0.3 完整性状态

| 范围 | 已核验 | 结果 |
|---|---:|---|
| 当前代码工作树 | 17/17 个 Git 仓库 | 活动表漏项 0、多计 0；实体/DDL-only 候选单独列示 |
| DBS 已审批在线域 | 6/6 组权限记录 | EM Service/Core、OK、ML Service/Core、BNPL 均已展开到数据库和表下拉 |
| DBS 实例下拉 | 53/53 个资产 | 23 MySQL + 26 OceanBase + 2 Redis + 2 MongoDB；能展开库表的写成三元组，未展开的只保留实例线索 |
| DataGrip 项目 `Opay` | 4/4 个数据源 | 5,505 张缓存物理表，按 schema 与代码逻辑表逐表对照 |

### 0.4 三条业务线的核心关系

命名归一：用户口径 `okask` 对应 OK（DBS/仓库常写 `okash`），`easemoni` 对应 EM，`merchant` / `ease-merchant-loan` 对应 ML。

| 业务线 | 产品核心 | 支付编排 | 渠道 | 上游/辅助 | 核心事实 |
|---|---|---|---|---|---|
| 现金贷 | `CashLoan/core-ng` | `CashLoan/pay-trade` | `CashLoan/pay-channel-ng` | `EM/service-ng`、`coupon-ng` | `orders` + `repay_plan` |
| 商户贷传统借据 | `ML/core` | `ML/pay-trade` | `ML/pay-channel` | `ML/service-ng`、`ml-reconciliation`、`ml-coupon` | `orders` + `repay_plan` |
| 商户贷 OD | `ML/ML_OD/ml-bill` | `ML/ML_OD/ml-od-trade` | 共享/外部渠道 | `ML/service-ng`、外部 Quota | `bill_account_info` + `bill_post_plan` + `bill_statement_info` |
| BNPL | `ease-pay-bill` | `ease-pay-trade` | `ease-pay-channel` | 外部 Quota/总账 | 账单账户、账单、交易、分配明细 |

### 0.5 需求关闭对照

| 原始要求 | 文档落点 | 关闭标准 |
|---|---|---|
| 梳理线上 / 线下库表关系 | 1.1/1.2、2.1/2.2、3.1/3.2 | 线上实时证据与 DataGrip/代码配置证据分层展示，不互相冒充 |
| 采用图二式结构 | 1.3、2.3、3.3 | 统一使用“服务 + 表 + 库 + 作用”四列 |
| 业务线作为一级标题 | `# 1` 现金贷、`# 2` 商户贷、`# 3` BNPL | 三条业务线均为 H1，命名归一在 0.4 明示 |
| 线上采用实例 + 库 + 表 | 1.1.1、2.1.1、3.1.1 | 已确认关系写成三元组；无法下钻的实例不外推库表 |
| 梳理代码仓库涉及表结构 | 1.4/1.5、2.4/2.5、3.4/3.5/3.6 | 17/17 仓库、415 次活动引用、候选表及线上/缓存差异均可追溯 |
| 多实例并行确认 | 6.3 | DBS 53/53 实例逐项列出，关系型与非关系型分开计数 |
| 测试库 / DBHub 权限处理 | 5 | 已区分“DBS 权限已审批”与“Codex 尚未注册 DBHub”；仅给出单 schema、只读最小配置边界 |

# 1. 现金贷（Easemoni / Okash）

## 1.1 线上实例 + 库 + 表族

品牌口径统一为：Easemoni = EM，Okash = OK。

### 1.1.1 在线实时确认

| 品牌 | 实例 | 库 | 表/表族（当前下拉可见） | 物理表数 | 证据 |
|---|---|---|---|---:|---|
| EM | `10.224.10.17-ng-em-core-ob-slave` | `microloan` | `orders`, `repay_plan`, `payment`, `user_e_trans`, `account_*`, `product*` 等 | 128 | `在线实时` + `权限记录 P-2270` |
| EM | 同上 | `microloan_coupon` | `coupon`, `account_coupon`, `account_coupon_history`, `del_*` | 5 | `在线实时` + `权限记录 P-2270` |
| EM | 同上 | `microloan_admin` | `admin`, `menu`, `role`, `operation*`, `coupon_task` 等 | 59 | `在线实时` + `权限记录 P-2270` |
| EM | `10.224.10.17-ng-em-service-ob-slave` | `okash` | EM Service 的 `okash_*`、KYC、申请、风控、营销和支付兼容表 | 510 | `在线实时` + `权限记录 P-2269` |
| EM | 同上 | `pay_channel` | `pay_channel_in`, `pay_channel_out`, `pay_channel_*_ext`, `pay_channel_adjust` 等 | 27 | `在线实时` + `权限记录 P-2269` |
| EM | 同上 | `tag_service` / `opay_finance_protocol` / `admin` | 标签 6 张 / 协议 3 张 / 管理 14 张 | 23 | `在线实时` + `权限记录 P-2269` |
| OK | `10.66.1.139-ng-okash-ob` | `microloan` | `orders`, `repay_plan`, `payment`, `user_e_trans`, `account_*` 等 | 21 | `在线实时` + `权限记录 P-2271` |
| OK | 同上 | `microloan_coupon` | `coupon`, `account_coupon`, `account_coupon_history` | 3 | `在线实时` + `权限记录 P-2271` |
| OK | 同上 | `okash` | `okash_*`, `pay_repayment_*`, `pay_b2c*`, `bluridge_mq_consume_result` 等 | 395 | `在线实时` + `权限记录 P-2271` |
| OK | 同上 | `pay_channel` / `message` | 渠道 29 张 / 消息 18 张 | 47 | `在线实时` + `权限记录 P-2271` |

查询历史提供了表级运行证据：`10.66.1.139-ng-okash-ob + microloan + orders`、`10.66.1.139-ng-okash-ob + pay_channel + pay_channel_out`、`10.66.1.139-ng-okash-ob + okash + pay_repayment_order`（2026-08-21 至 2026-08-24）。

### 1.1.2 页面可见但当前账号未展开库表

| 品牌 | 实例 | 本轮状态 |
|---|---|---|
| EM | `10.215.25.89-ng-easemoni-service-slave`, `10.215.40.147-ng-easemoni-bi`, `10.215.56.206-ng-easemoni-collection-slave`, `10.215.91.109-ng-easemoni-core-slave`, `10.215.99.85-ng-easemoni-service-master` | `在线实时`确认实例存在；数据库下拉为空，其中 service-master 返回页面提示；不得写成已确认三元组 |
| OK | `10.217.122.197-ng-okash-service-slave`, `10.217.2.80-ng-okash-bi`, `10.217.63.40-ng-okash-core-slave`, `10.217.97.163-ng-okash-collection-slave` | `在线实时`确认实例存在；数据库下拉为空，不并入已确认三元组 |

代码配置仍可补充部署候选：EM Core 指向 `10.215.91.109/{microloan,microloan_admin}`，OK Core 指向 `10.217.63.40/{microloan,microloan_admin}`，但这些属于 `代码配置`，与上面的 `在线实时` 证据分开使用。

## 1.2 线下 / 测试库

| DataGrip 数据源 | 引擎 | 相关库 | 缓存表数 | 与代码链路的关系 |
|---|---|---|---:|---|
| `em 159.138.165.0:2883` | OceanBase 4.3.5.3 | `okash` / `yinni_microloan` / `pay_channel` | 537 / 153 / 30 | 覆盖 EM Service、Core、Trade/Channel 候选表；快照 2026-08-07 |
| `ok 159.138.174.6:3306` | MySQL 5.7.37 | `admin` / `ng_collection` / `okash` / `pay_channel` / `yinni_microloan` / `yinni_microloan_admin` | 17 / 7 / 428 / 27 / 150 / 57（合计 686） | 覆盖 OK 管理、催收、Service、Core、Trade/Channel；快照 2026-08-13 |
| 代码 EM 测试地址 | MySQL | `10.220.0.128/{yinni_microloan,yinni_microloan_admin,microloan_coupon,okash}` | 未实时读取 | `em-test-env.xml`、Trade/Bill 本地配置 |
| 代码 NG 测试地址 | MySQL | `10.220.2.99/{yinni_microloan,yinni_microloan_admin,microloan_coupon,okash}` | 未实时读取 | `ng-test-env.xml`、`ok-pay-trade-test-env.xml` |

DataGrip 中 `microloan`、`microloan_admin` 未出现在 EM 缓存 schema；`microloan_coupon`/`yinni_microloan_admin` 在部分连接有 schema 名但无 table 节点。这表示缓存未覆盖，不能解释为库或表不存在。

## 1.3 主要表（图二口径）

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| EM Service | `okash_user`, `okash_user_profile`, `okash_user_auth` | `okash` | 用户、资料、认证底座 |
| EM Service | `okash_loan_apply`, `okash_loan_apply_ext`, `okash_loan_apply_temp` | `okash` | 贷款申请主单、扩展和临时态 |
| EM Service | `okash_risk_result`, `okash_user_risk_auth`, `okash_anti_fraud_*` | `okash` | 授信/用信风控及反欺诈结果 |
| Core | `orders` | `microloan` / `yinni_microloan` | 借据主单、合同金额和状态 |
| Core | `orders_fee_float` | 同上 | 申请/放款费率快照 |
| Core | `repay_plan` | 同上 | 每期应还、已还、减免、罚息 |
| Core | `payment` | 同上 | 一次 Core 入账总凭证 |
| Core | `user_e_trans` | 同上 | 入账科目明细与 `batch_no` 幂等事实 |
| Core | `account_balance`, `account_balance_flow` | 同上 | 多还余额及变动流水 |
| Core | `core_account_fund_balance_record` | 同上 | 当前分支已写入的管理费/VAT 资金记录 |
| Core | `order_message_record` | 同上 | 本地消息发送记录与补偿 |
| Pay Trade | `pay_repayment_order` | `okash` | 用户一次还款总单 |
| Pay Trade | `pay_repayment_core_detail` | `okash` | 还款拆分到各借据/Core 的处理明细 |
| Pay Trade | `pay_b2c` | `okash` | 放款/出款业务单 |
| Pay Channel | `pay_channel_in`, `pay_channel_out` | `pay_channel` | 渠道入金/出金本地事实 |
| Coupon | `coupon`, `account_coupon`, `account_coupon_expired` | `microloan_coupon` | 券模板、用户券与过期归档 |

## 1.4 代码侧完整逻辑表清单

### EM `service-ng`（92 张活动表 + 4 张仅实体候选）

| 表组 | 完整逻辑表 |
|---|---|
| 用户/KYC/账户/终端（40） | `okash_user`, `okash_user_profile`, `okash_user_profile_history`, `okash_user_data`, `okash_user_auth`, `okash_user_level`, `okash_user_image`, `okash_user_hardware`, `device_info`, `group_config`, `okash_user_utm`, `user_install_market_record`, `okash_channle_register_user`, `okash_contact_information`, `okash_feedback_record`, `okash_user_redirect_log`, `okash_user_push_token`, `okash_message_black_user`, `okash_activity`, `okash_bank`, `okash_user_bankaccount`, `okash_card`, `okash_user_virtual`, `okash_user_wema_bank_virtual`, `okash_user_bvn`, `okash_user_bvn_back_history`, `user_upload_bvn_record`, `nibss_igree_bvn_flow`, `okash_user_profile_nin`, `t_user_profile_nin`, `okash_user_kyc_log`, `okash_user_face_success_record`, `okash_machine_verify`, `user_check_mobile_record`, `user_change_mobile_record`, `user_second_release_mobile_record`, `okash_opay_authorised_user`, `okash_opay_nin_authorised_user`, `okash_user_okra`, `okash_user_risk_rank_record` |
| 贷款/风控/认证/展期（30） | `okash_loan_apply`, `okash_loan_apply_temp`, `okash_loan_apply_ext`, `okash_loan_apply_mark`, `okash_loan_face_check`, `okash_loan_increase`, `okash_promo_work_main`, `abandon_loan_user_tags`, `okash_confirm_auto_debit_protocol`, `okash_confirm_relation`, `okash_user_questionnaires`, `okash_user_standby_mobile`, `okash_user_standby_mobile_backup`, `okash_risk_result`, `okash_risk_audit_record`, `okash_anti_fraud_check`, `okash_anti_fraud_info`, `okash_user_verify_pep_hit`, `okash_user_verify_pep_result`, `okash_user_risk_auth`, `okash_user_temp_risk_auth`, `okash_user_confirm_risk_auth`, `okash_user_reoffer_auth`, `okash_user_plus_offers_risk_auth`, `okash_user_diversion`, `offline_auth_message_record`, `okash_defer_auth`, `okash_defer_confirm_record`, `okash_user_apply_protocol`, `okash_risk_coupon_record` |
| 营销/优惠券/配置/运维（22） | `coupon_grant_record`, `okash_loaning_coupon_record`, `okash_order_coupon_apply_record`, `okash_market_rules`, `okash_market_record`, `okash_market_record_center`, `marketing_realtime_hit_record`, `marketing_realtime_hit_step_record`, `mg_raffle_record`, `mg_supplies_marketing_record`, `okash_home_pop_windows_record`, `okash_entry_config`, `okash_resource_position`, `okash_otp_channel_configuration`, `app_secret`, `sys_dict`, `sys_cache`, `okash_version_upgrade`, `okash_investigation_config`, `okash_user_investigation`, `okash_retry_log`, `dau_statistical` |
| 仅实体候选（不计活动表） | `okash_promo_content`, `okash_pos_transaction_query_record`, `okash_remita_salary_history`, `okash_servant_loan` |

注：三组活动表为 40 + 30 + 22 = 92。`okash_entry_config` 的 SQL 为显式跨库 `okash.okash_entry_config`。

### `CashLoan/core-ng`（25）

`orders`, `orders_fee_float`, `core_account_fund_balance_record`, `repay_plan`, `repay_plan_grace_record`, `repay_plan_overdue`, `repay_plan_overdue_record`, `payment`, `user_e_trans`, `deleted_payment`, `deleted_user_e_trans`, `deleted_repay_plan`, `account_base`, `account_profile`, `account_balance`, `account_balance_flow`, `defer_orders`, `defer_orders_pay_record`, `remissions`, `reduce_record`, `remission_payment`, `remission_user_e_trans`, `order_message_record`, `product`, `product_opt_record`。

### `CashLoan/coupon-ng`（3）

`coupon`, `account_coupon`, `account_coupon_expired`。

### `CashLoan/pay-channel-ng`（15）

`pay_channel_in`, `pay_channel_in_ext`, `pay_channel_in_batch`, `pay_channel_in_ext_batch`, `pay_channel_out`, `pay_channel_out_ext`, `pay_channel_out_send_log`, `pay_channel_adjust`, `pay_channel_in_notify_unusual`, `pay_channel_out_unusual_trans`, `pay_channel_opay_cash_back`, `opay_sub_detail_temporary_record`, `okash_virtual_random`, `okash_wema_bank_virtual_random`, `pay_channel_in_wema_bank_ext`。

### `CashLoan/pay-trade`（38）

`pay_repayment_order`, `pay_repayment_core_detail`, `pay_repayment_manual`, `pay_repayment_batch_deduct`, `pay_repayment_overpaid`, `pay_b2c`, `pay_b2c_inner`, `pay_b2c_loan_ext`, `pay_withdrawal`, `pay_biz_adjust`, `pay_trade_adjust`, `pay_amount_whitelist`, `settle_account_detail_by_given_date`, `deffer_reduce_info`, `pay_bfree_reduce_info`, `repay_collection_batch_reduce_config`, `repay_collection_reduce_info`, `okash_loan_apply`, `okash_loan_apply_mark`, `okash_order_coupon_apply_record`, `okash_repay_back`, `okash_defer_confirm_record`, `given_bfree_data`, `okash_user`, `okash_user_profile`, `okash_user_bankaccount`, `okash_bank`, `okash_card`, `okash_card_bin_info`, `okash_user_apply_protocol`, `okash_user_protocol`, `okash_confirm_auto_debit_protocol`, `okash_user_virtual`, `okash_user_wema_bank_virtual`, `okash_ussd_bank`, `okash_ussd_transfer_record`, `bluridge_mq_consume_result`, `okash_retry_log`。

`CashLoan/blieridge-into-gateway` 当前工作树未发现 datasource、Mapper、SQL 或 ORM 持久化模型。

## 1.5 代码 / 线上 / DataGrip 逐表覆盖

覆盖数按代码活动逻辑表计算；“未见”表示当前 DBS 表下拉或 DataGrip 快照没有同名表，不等于已证明生产缺表。

| 代码域 | 代码活动表 | DBS 在线覆盖 | DataGrip 缓存覆盖 | 当前差异 |
|---|---:|---:|---:|---|
| EM `service-ng` → EM `okash` | 92 | 92/92 | 92/92 | 无表名缺口 |
| CashLoan Core → EM `microloan + microloan_admin` | 25 | 24/25 | 23/25 | 在线未见 `core_account_fund_balance_record`；缓存未见 admin 库的 `product`, `product_opt_record` |
| CashLoan Core → OK `microloan`（线上）/ `yinni_microloan + yinni_microloan_admin`（缓存） | 25 | 20/25 | 23/25 | 在线未见 `core_account_fund_balance_record`, `defer_orders`, `defer_orders_pay_record`, `product`, `product_opt_record`；缓存仅未见两张 `defer_*` |
| CashLoan Coupon → EM/OK `microloan_coupon` | 3 | EM 2/3；OK 2/3 | EM 0/3；OK 0/3 | 在线均未见 `account_coupon_expired`；DataGrip 只有 schema 节点，无 table 节点 |
| CashLoan Pay Channel → EM/OK `pay_channel` | 15 | EM 15/15；OK 15/15 | EM 15/15；OK 15/15 | 无表名缺口 |
| CashLoan Pay Trade → EM `okash` | 38 | 38/38 | 38/38 | 无表名缺口 |
| CashLoan Pay Trade → OK `okash` | 38 | 34/38 | 37/38 | 在线未见 `pay_b2c_inner`, `pay_b2c_loan_ext`, `deffer_reduce_info`, `okash_defer_confirm_record`；缓存仅未见最后一张 |

`blieridge-into-gateway` 为 0 张关系表，因此不参与覆盖率分母。上述差异保留为版本/部署核验项，不能用另一品牌或旧快照中的同名表替代。

# 2. 商户贷（ML）

## 2.1 线上实例 + 库 + 表族

### 2.1.1 在线实时确认

| 实例 | 库 | 表/表族（当前下拉可见） | 物理表数 | 证据 |
|---|---|---|---:|---|
| `10.222.6.88-ease-merchant-loan-service-slave` | `ml_ease` | `ka_*`, `od_*`, `okash_*`, `quota_*`, `pay_repayment_*`, `bluridge_mq_consume_result` 等 | 228 | `在线实时` + `权限记录 P-2268` |
| 同上 | `ml_admin` / `ml_protocol` | 管理 10 张 / 协议 4 张 | 14 | `在线实时` + `权限记录 P-2268` |
| 同上 | `ml_od_trade` | 9 张 `tra_*`，与代码清单完全覆盖 | 9 | `在线实时` + `权限记录 P-2268` |
| 同上 | `ml_risk_gateway` / `okash_risk` | 风控网关 21 张 / 风控 37 张 | 58 | `在线实时` + `权限记录 P-2268` |
| 同上 | `ml_channel` / `ml_message` | 渠道 9 张 / 消息 20 张；`ml_gatekeeper` 当前为空 | 29 | `在线实时` + `权限记录 P-2268` |
| `10.222.6.130-ease-merchant-loan-core-slave` | `ml_microloan` | `orders`, `repay_plan`, `payment`, `user_e_trans`, `core_account_fund_balance_record` 等 | 22 | `在线实时` + `权限记录 P-2272` |
| 同上 | `ml_microloan_coupon` | `coupon`, `account_coupon` | 2 | `在线实时` + `权限记录 P-2272` |
| 同上 | `ml_bill` | 17 张 `bill_*`，与 `ML_OD/ml-bill` 代码清单完全覆盖 | 17 | `在线实时` + `权限记录 P-2272` |

查询历史提供了两条表级运行证据：`10.222.6.88 + ml_ease + bluridge_mq_consume_result`（2026-08-26）和 `10.222.6.130 + ml_bill + bill_post_plan_detail`（2026-08-25）。

### 2.1.2 页面可见但当前账号未展开库表

| 实例 | 本轮状态 |
|---|---|
| `10.221.16.112-ng-ml-service-slave`, `10.221.16.20-ng-ml-bi-slave` | `在线实时`确认实例存在；数据库下拉为空 |
| `10.222.6.101-ease-merchant-loan-bi-master`, `10.222.6.147-ease-merchant-loan-collection-slave` | `在线实时`确认实例存在；数据库下拉为空，不并入已确认三元组 |

实时库名纠正了原候选：Core 使用 `ml_microloan`，Coupon 使用 `ml_microloan_coupon`；`yinni_microloan` 只保留为 DataGrip 历史/测试候选。服务实例中的实时渠道库名是 `ml_channel`，DataGrip 测试快照中的名称是 `ml_pay_channel`，不能直接视为同一物理库。

## 2.2 线下 / 测试库

| DataGrip 数据源 | 库 | 缓存表数 | 代码归属 |
|---|---|---:|---|
| `ml 110.238.77.34:3306` | `ml_ease` | 218 | `ML/service-ng` |
| 同上 | `ml_microloan` | 22 | `ML/core` API 候选 |
| 同上 | `yinni_microloan` | 152 | `ML/core` Batch/历史候选 |
| 同上 | `ml_bill` | 17 | `ML/ML_OD/ml-bill`；与代码 17 张完全同名覆盖 |
| 同上 | `ml_od_trade` | 9 | `ML/ML_OD/ml-od-trade`；与代码 9 张完全同名覆盖 |
| 同上 | `ml_pay_channel` | 9 | `ML/pay-channel` 运行子集 |
| 同上 | `ng_collection` | 8 | 催收辅助；不并入当前 ML 仓库表计数 |

## 2.3 主要表（图二口径）

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| ML Service | `ka_loan_apply`, `ka_account_detail` | `ml_ease` | 商户贷款申请与账户信息 |
| ML Service | `od_apply`, `od_approval_order`, `od_quota_*` | `ml_ease` | OD 申请、审批和额度动作快照 |
| 传统 Core | `orders`, `orders_fee_float` | `ml_microloan` / `yinni_microloan` | 借据主单与费率快照 |
| 传统 Core | `repay_plan` | 同上 | 分期应收与还款计划 |
| 传统 Core | `payment`, `user_e_trans` | 同上 | Core 入账凭证与科目明细 |
| 传统 Core | `core_account_fund_balance_record` | 同上 | 管理费/VAT 资金变化记录 |
| OD Bill | `bill_account_info` | `ml_bill` | OD 循环账户与账期指针 |
| OD Bill | `bill_post_plan`, `bill_post_plan_detail` | `ml_bill` | 未出账应收计划和交易级明细 |
| OD Bill | `bill_statement_info`, `bill_statement_trans` | `ml_bill` | 已出账 statement 与交易清单 |
| OD Trade | `tra_transfer_transaction` | `ml_od_trade` | 用信/消费请求与 Bill 回执 |
| OD Trade | `tra_repayment_order` | `ml_od_trade` | 主动/自动还款订单 |
| Pay Trade | `pay_repayment_order`, `pay_repayment_core_detail` | 物理库待定 | 支付还款单及 Core posting 明细 |
| Pay Channel | `pay_channel_in`, `pay_channel_out` | `ml_pay_channel` 候选 | 渠道入金/出金事实 |
| Reconciliation | `settle_account_detail*`, `pay_channel_*` | `okash` + `opay_ods` | 支付、渠道和外部数据对账读写模型 |

## 2.4 代码侧完整逻辑表清单

### `ML/ML_OD/ml-bill`（17）

`bill_account_info`, `bill_account_change_log`, `bill_rule_detail`, `bill_post_plan`, `bill_post_plan_detail`, `bill_statement_info`, `bill_statement_trans`, `bill_transfer_transaction`, `bill_repay_transaction`, `bill_refund_transaction`, `bill_refund_transaction_detail`, `bill_fee_transaction`, `bill_interest_transaction`, `bill_interest_transaction_detail`, `bill_penalty_transaction`, `bill_penalty_transaction_detail`, `bill_mq_exception`。

### `ML/ML_OD/ml-od-trade`（9）

`tra_transfer_transaction`, `tra_refund_transaction`, `tra_repayment_order`, `tra_op_freeze_detail`, `tra_op_confirm_detail`, `tra_op_unfreeze_detail`, `tra_op_release_detail`, `tra_user_protocol`, `tra_mq_exception`。

### `ML/core`（25）

`orders`, `orders_fee_float`, `repay_plan`, `repay_plan_grace_record`, `repay_plan_overdue`, `repay_plan_overdue_record`, `payment`, `user_e_trans`, `deleted_payment`, `deleted_user_e_trans`, `deleted_repay_plan`, `account_base`, `account_profile`, `account_balance`, `account_balance_flow`, `remissions`, `reduce_record`, `remission_payment`, `remission_user_e_trans`, `order_message_record`, `product`, `product_opt_record`, `core_account_fund_balance_record`, `out_balance_customer`, `withholding_white_list`。

### `ML/ml-coupon`（2）

`coupon`, `account_coupon`。

### `ML/ml-reconciliation`（22）

`pay_channel_in`, `pay_channel_in_ext`, `pay_channel_in_wema_bank_ext`, `pay_channel_out`, `pay_channel_out_ext`, `pay_channel_adjust`, `pay_channel_opay_cash_back`, `opay_sub_detail_temporary_record`, `pay_b2c`, `pay_b2c_opay`, `pay_repayment`, `pay_trade_adjust`, `settle_account_detail`, `settle_account_detail_by_given_date`, `bfree_repay_back_detail`, `okash_adjustment_record`, `okash_card`, `okash_card_refund`, `okash_repay_back`, `okash_cr_credit_info`, `okash_crc_credit_info`, `okash_crc_individual_borrower`。

### `ML/pay-channel`（10 张确认 + 4 张候选）

确认：`pay_channel_in`, `pay_channel_in_ext`, `pay_channel_out`, `pay_channel_out_ext`, `pay_channel_out_send_log`, `pay_channel_adjust`, `pay_channel_in_notify_unusual`, `pay_channel_out_unusual_trans`, `pay_channel_opay_cash_back`, `opay_sub_detail_temporary_record`。  
仅模型候选：`okash_virtual_random`, `pay_channel_in_batch`, `pay_channel_in_ext_batch`, `remita_decuction_history`。

### `ML/pay-trade`（21）

`pay_repayment_order`, `pay_repayment_core_detail`, `pay_repayment_overpaid`, `pay_b2c`, `pay_withdrawal`, `okash_loan_apply`, `okash_loan_apply_mark`, `ka_loan_apply`, `ka_account_detail`, `ka_guarantor_person`, `okash_user`, `okash_opay_authorised_user`, `okash_bank`, `okash_ussd_bank`, `okash_user_apply_protocol`, `okash_opay_sign`, `pay_opay_deduct_sign`, `okash_order_coupon_apply_record`, `opay_activity_record`, `okash_repay_back`, `bluridge_mq_consume_result`。

### `ML/service-ng`（89 张确认 + 3 张候选）

| 表组 | 完整逻辑表 |
|---|---|
| 配置/资源 | `app_secret`, `group_config`, `sys_cache`, `sys_dict`, `sys_district`, `okash_otp_channel_configuration`, `okash_resource_position`, `okash_version_upgrade` |
| 用户/账户/KYC | `device_info`, `merchant_deact_bvn_operation_record`, `okash_activity`, `okash_bank`, `okash_card`, `okash_channle_register_user`, `okash_contact_information`, `okash_feedback_record`, `okash_opay_authorised_user`, `okash_user`, `okash_user_auth`, `okash_user_bankaccount`, `okash_user_bvn`, `okash_user_bvn_back_history`, `okash_user_data`, `okash_user_hardware`, `okash_user_image`, `okash_user_level`, `okash_user_okra`, `okash_user_profile`, `okash_user_profile_history`, `okash_user_redirect_log`, `okash_user_risk_rank_record`, `okash_user_utm`, `okash_user_virtual`, `user_change_mobile_record`, `user_check_mobile_record`, `user_install_market_record` |
| 风控/拒绝/调查 | `okash_anti_fraud_check`, `okash_anti_fraud_info`, `okash_investigation_config`, `okash_machine_verify`, `okash_risk_result`, `okash_user_investigation`, `okash_user_verify_pep_hit`, `okash_user_verify_pep_result`, `reject_code_cooling_period`, `reject_code_type`, `reject_record` |
| 现金贷兼容表 | `okash_loan_apply`, `okash_loan_apply_mark`, `okash_loan_apply_temp`, `okash_loan_face_check`, `okash_loan_increase`, `okash_loaning_coupon_record`, `okash_order_coupon_apply_record`, `okash_risk_coupon_record`, `okash_user_questionnaires`, `okash_retry_log` |
| OD/循环额度 | `od_apply`, `od_apply_file`, `od_approval_order`, `od_guarantor_person`, `kyc_pic_file`, `okash_confirm_relation`, `okash_user_confirm_risk_auth`, `okash_user_diversion`, `okash_user_risk_auth`, `okash_user_risk_auth_log`, `okash_user_standby_mobile`, `okash_user_standby_mobile_backup`, `second_user_make_quota_request` |
| KA 商户贷 | `ka_account_detail`, `ka_guarantor_person`, `ka_loan_apply`, `ka_loan_apply_image`, `ka_loan_approval_order`, `ka_operation_record`, `ka_risk_log` |
| 营销 | `mg_raffle_record`, `mg_supplies_marketing_record`, `okash_home_pop_windows_record`, `okash_market_record`, `okash_market_record_center`, `okash_market_rules`, `opay_activity_record`, `okash_promo_work_main` |
| 统计/跨库/支付读取 | `dau_statistical`, `okash_entry_config`, `pay_b2c`, `pay_b2c_opay` |
| 仅模型候选 | `okash_pos_transaction_query_record`, `okash_promo_content`, `okash_servant_loan` |

## 2.5 代码 / 线上 / DataGrip 逐表覆盖

| 代码域 | 代码活动表 | DBS 在线覆盖 | DataGrip 缓存覆盖 | 当前差异 |
|---|---:|---:|---:|---|
| `ML_OD/ml-bill` → `ml_bill` | 17 | 17/17 | 17/17 | 无表名缺口 |
| `ML_OD/ml-od-trade` → `ml_od_trade` | 9 | 9/9 | 9/9 | 无表名缺口 |
| `ML/core` → `ml_microloan` | 25 | 22/25 | 22/25 | 两侧均未见 `repay_plan_overdue`, `account_profile`, `product_opt_record` |
| `ML/core` → DataGrip 历史候选 `yinni_microloan` | 25 | 不作为当前线上库 | 20/25 | 缓存未见 `product`, `product_opt_record`, `core_account_fund_balance_record`, `out_balance_customer`, `withholding_white_list` |
| `ML/ml-coupon` → `ml_microloan_coupon` | 2 | 2/2 | 0/2 | 在线完整；DataGrip 只有 schema 节点，无 table 节点 |
| `ML/pay-channel` → `ml_channel`（线上）/ `ml_pay_channel`（缓存） | 10 | 9/10 | 9/10 | 两侧均未见 `opay_sub_detail_temporary_record`；库名不同已保留 |
| `ML/pay-trade` → `ml_ease` | 21 | 20/21 | 20/21 | 两侧均未见 `okash_ussd_bank` |
| `ML/service-ng` → `ml_ease` | 89 | 70/89 | 70/89 | 未见 19 张：`group_config`, `okash_resource_position`, `device_info`, `okash_user_level`, `okash_user_okra`, `okash_user_profile_history`, `okash_user_redirect_log`, `okash_user_virtual`, `okash_investigation_config`, `okash_user_investigation`, `okash_loan_apply_temp`, `mg_raffle_record`, `mg_supplies_marketing_record`, `okash_market_record`, `okash_market_record_center`, `okash_market_rules`, `okash_promo_work_main`, `dau_statistical`, `pay_b2c_opay` |
| `ML/ml-reconciliation` | 22 | 正确生产实例未绑定 | 当前项目无匹配双数据源 | 代码明确拆为 `okash` 15 张和 `opay_ods` 7 张；现有 ML DBS 实例不暴露这两个库，不能拿 `ml_ease/ml_channel` 的同名表替代 |

`ml-reconciliation` 当前仓配置给出两个测试/开发连接线索：MySQL `okash` 与 `159.138.169.217:9030/opay_ods`；账号口令不进入本清单。生产值可能被 Apollo 覆盖，故只保留为 `代码配置`，不标成在线运行事实。

# 3. BNPL

## 3.1 线上实例 + 库 + 表族

### 3.1.1 在线实时确认

| 实例 | DBS 库名 | 代码库名 | 表/表族（当前下拉可见） | 当前物理表数 | 证据 |
|---|---|---|---|---:|---|
| `10.226.10.147-ng_loan_collect-ob` | `bnpl_ep_bill` | `ep_bill` | 29/30 个活动逻辑表族可见；`funds_accounting_info` 与 `funds_accounting_detail` 各 100 个分片，`mq_exception_record` 已可见 | 233 | `在线实时` + `权限记录 P-2273` |
| 同上 | `bnpl_ep_pay_trade` | `ep_pay_trade` | 13/13 个代码逻辑表全部可见，包括 `tra_*transaction`, `tra_op_*`, `tra_*protocol`, MQ 与余额记录 | 20 | `在线实时` + `权限记录 P-2273` |
| 同上 | `bnpl_ep_pay_channel` | `ep_pay_channel` | `pay_channel_in`, `pay_channel_out`, `pay_channel_opay_cash_back`, `channel_mq_exception`，4/4 全部可见 | 4 | `在线实时` + `权限记录 P-2273` |

实时差异：代码活动表 `bill_repay_trans_detail` 未出现在 `bnpl_ep_bill` 当前表结构下拉，其余 29 个 Bill 活动逻辑表均可见。该项是运行结构核对缺口，不能以代码或旧缓存替代确认。

### 3.1.2 页面可见但当前账号未展开库表

| 环境 | 实例 | 本轮状态 |
|---|---|---|
| BNPL 生产 MySQL | `10.226.10.216-ng-bnpl-prod-master` | `在线实时`确认实例名和当前地址；数据库下拉为空 |
| BNPL Prepare | `192.25.3.99-ng-bnpl-prepare-master`, `192.25.3.49-ng-bnpl-prepare-slave01` | `在线实时`确认实例存在；数据库下拉为空 |
| BNPL OceanBase 分库入口 | `10.226.10.3-ep_bill-ob-slave`, `10.226.10.3-ep_trade_channel-ob-slave`, `10.220.4.60-ng_bnpl_test-ob` | `在线实时`确认实例存在；当前账号未展开数据库；生产三元组以上述已授权汇总实例为准 |
| BNPL 本地测试配置 | `10.220.0.128:3306` | `代码配置`仅确认 `ep_bill`，不能外推生产 |

## 3.2 线下 / DataGrip

| DataGrip 数据源 | 引擎 | 库 | 缓存物理表数 | 代码/运行对照 |
|---|---|---|---:|---|
| `bnpl 159.138.165.0:2883` | OceanBase 4.3.5.3 | `ep_bill` | 1,027 | 代码 30 个活动逻辑表；缓存含 10 个 100 分片族和非分片表 |
| 同上 | 同上 | `ep_pay_trade` | 1,286 | 代码 13 个活动逻辑表；缓存大量 `{0..127}` 分表 |
| 同上 | 同上 | `ep_pay_channel` | 260 | 代码 4 个活动逻辑表；缓存 `pay_channel_in`、CashBack 的 128 分片族 |
| 同上 | 同上 | `ep_quota_center` | 775 | 外部额度事实库；当前三个 BNPL 仓库只保存额度动作快照 |
| 同上 | 同上 | 其他业务 schema | 851 | `ep_admin`, `ep_collection`, `ep_crb`, `ep_message`, `ep_protocol`, `ep_quota_center`, `ep_risk_gateway`, `ep_service`, `ep_user`, `marketing_server`, `task_center` |
| 同上 | 同上 | 系统 schema | 240 | `information_schema` 4、`mysql` 18、`oceanbase` 218；不计业务表 |

当前分支新增/漂移：2026-08-03 DataGrip 缓存未出现 `funds_accounting_info`, `funds_accounting_detail`, `mq_exception_record`；2026-08-31 DBS 实时表结构已经出现三者，其中两个 `funds_accounting_*` 表族各有 100 个物理分片。旧缓存需要刷新。

## 3.3 主要表（图二口径）

| 服务 | 表 | 库 | 作用 |
|---|---|---|---|
| Trade | `tra_consume_transaction` | `ep_pay_trade` | 普通消费交易及支付/Bill 标记 |
| Trade | `tra_installment_transaction` | `ep_pay_trade` | 分期消费交易与分期期数 |
| Trade | `tra_refund_transaction` | `ep_pay_trade` | 退款请求、原交易关系和结果 |
| Trade | `tra_repayment_order` | `ep_pay_trade` | BNPL 账单还款支付单 |
| Trade | `tra_op_{freeze,confirm,unfreeze,release}_detail` | `ep_pay_trade` | 额度动作本地快照，不是额度总账 |
| Bill | `bill_account_info` | `ep_bill` | 账单账户、规则、费率和账期参数 |
| Bill | `user_bill_into_account` | `ep_bill` | 未出账累计应收 |
| Bill | `user_bill_out_account` | `ep_bill` | 已出账应收、还款/减免/退款金额和状态 |
| Bill | `user_bill_detail` | `ep_bill` | 账单与来源交易的桥接明细 |
| Bill | `bill_consume_transaction`, `bill_fee_transaction` | `ep_bill` | 消费本金和费用入账事实 |
| Bill | `bill_repay_transaction`, `bill_repay_trans_detail` | `ep_bill` | 还款进入 Bill 及对账单分配明细 |
| Bill | `bill_refund_transaction`, `bill_refund_trans_detail` | `ep_bill` | 退款入账及分配明细 |
| Bill | `user_overpaid_balance`, `user_overpaid_balance_detail` | `ep_bill` | 多还余额当前值和来源变动 |
| Bill | `funds_accounting_info`, `funds_accounting_detail` | `ep_bill` | 当前分支新增的资金记账主表/明细 |
| Channel | `pay_channel_in`, `pay_channel_out` | `ep_pay_channel` | 渠道入金/出金终态候选 |

## 3.4 代码侧完整逻辑表清单

### `BNPL/ease-pay-trade`（13）

`tra_consume_transaction`, `tra_installment_transaction`, `tra_refund_transaction`, `tra_repayment_order`, `tra_withdrawal_transaction`, `tra_op_freeze_detail`, `tra_op_confirm_detail`, `tra_op_unfreeze_detail`, `tra_op_release_detail`, `tra_loan_protocol`, `tra_user_protocol`, `tra_bnpl_mq_exception`, `tra_merchant_balance_record`。

### `BNPL/ease-pay-bill`（30 张活动表 + 1 张 DDL-only 候选）

`bill_account_info`, `bill_account_change_log`, `bill_account_balance`, `bill_account_balance_op_detail`, `bill_rule_detail`, `bill_consume_transaction`, `bill_installment_transaction`, `bill_installment_plan`, `bill_fee_transaction`, `bill_penalty_transaction`, `bill_penalty_record`, `bill_post_detail`, `bill_repay_transaction`, `bill_repay_trans_detail`, `bill_refund_transaction`, `bill_refund_trans_detail`, `bill_reduce_transaction`, `bill_reduce_record`, `bill_batch_result`, `bill_batch_sharding_key_record`, `user_bill_into_account`, `user_bill_into_account_archive`, `user_bill_out_account`, `user_bill_detail`, `user_overpaid_balance`, `user_overpaid_balance_detail`, `order_message_record`, `mq_exception_record`, `funds_accounting_info`, `funds_accounting_detail`。  
DDL-only 候选：`user_bill_rule_info`（仓内 DDL 有表，但当前活动 Entity/Mapper 集合未纳入）。

### `BNPL/ease-pay-channel`（4）

`pay_channel_in`, `pay_channel_out`, `pay_channel_opay_cash_back`, `channel_mq_exception`。

## 3.5 BNPL 物理分表关系（DataGrip 缓存）

| 库 | 逻辑表族 | 物理展开 |
|---|---|---|
| `ep_bill` | `bill_consume_transaction`, `bill_fee_transaction`, `bill_installment_transaction`, `bill_installment_plan`, `bill_penalty_transaction`, `bill_post_detail`, `bill_repay_transaction`, `bill_repay_trans_detail`, `user_bill_detail`, `user_bill_out_account` | 多数为 `_0000.._0099` 共 100 分片；部分同时保留无后缀基表 |
| `ep_pay_trade` | `tra_consume_transaction`, `tra_installment_transaction`, `tra_loan_protocol`, `tra_op_confirm_detail`, `tra_op_freeze_detail`, `tra_op_release_detail`, `tra_op_unfreeze_detail`, `tra_refund_transaction`, `tra_repayment_order`, `tra_withdrawal_transaction` | `_0.._127` 共 128 分片；部分同时保留无后缀基表 |
| `ep_pay_channel` | `pay_channel_in`, `pay_channel_opay_cash_back` | `_0.._127` 共 128 分片；并有无后缀表 |

## 3.6 代码 / 线上 / DataGrip 逐表覆盖

| 代码域 | 代码活动表 | DBS 在线覆盖 | DataGrip 缓存覆盖 | 当前差异 |
|---|---:|---:|---:|---|
| `BNPL/ease-pay-bill` → `bnpl_ep_bill` / `ep_bill` | 30 | 29/30 | 27/30 | 在线未见 `bill_repay_trans_detail`；旧缓存未见 `mq_exception_record`, `funds_accounting_info`, `funds_accounting_detail`。两份证据的并集覆盖 30/30，但显示部署时间漂移 |
| `BNPL/ease-pay-trade` → `bnpl_ep_pay_trade` / `ep_pay_trade` | 13 | 13/13 | 13/13 | 无表名缺口 |
| `BNPL/ease-pay-channel` → `bnpl_ep_pay_channel` / `ep_pay_channel` | 4 | 4/4 | 4/4 | 无表名缺口 |
| Bill DDL-only `user_bill_rule_info` | 不计活动表 | 在线可见 | 缓存可见 | 运行表存在，但当前工作树没有活动 Entity/Mapper 调用，继续保持候选状态 |

线上库使用 DBS 的租户前缀 `bnpl_`，代码与 DataGrip 使用 `ep_*`；本表只按已验证实例和权限记录建立别名，不把名称相似当作自动等价。

# 附录

## 4. 跨业务线共享与不可混淆项

| 对象 | 关系 | 不能据此推断 |
|---|---|---|
| `orders/repay_plan/payment/user_e_trans` | 现金贷与 ML 传统 Core 同源模型 | 两条线一定同库、同版本或共享生产数据 |
| `pay_channel_*` | CashLoan、ML、BNPL 都有渠道模型 | 同名表一定属于同一实例或同一 owner |
| `okash_*` | EM、CashLoan Trade、ML Service/Trade/Reconciliation 均有访问 | 表名前缀能证明唯一业务归属 |
| `bill_flag` / `post_bill_status` | 表示跨服务处理阶段 | 支付、账单、额度、渠道和总账已同时成功 |
| DataGrip 连接名 `ml/em/ok/bnpl` | 本机数据源标签 | 连接中所有 schema 都属于该标签业务线 |

## 5. DBHub 状态与最小修复边界

本轮确认 `dbhub` 未出现在 Codex 的 MCP 工具中，原因不是库表权限拒绝，而是 `/Users/guozhixiang/.codex/config.toml` 未注册 `[mcp_servers.dbhub]`。仓库 `/Users/guozhixiang/Agent/dbhub/plugin/.mcp.json` 属于 Claude 插件配置，不会被 Codex 自动加载。

没有直接写入配置，原因如下：

1. 四个 DataGrip JDBC URL 都只到 host/port，没有固定数据库名；直接复用会把整台服务暴露给 DBHub。
2. DataGrip 凭据保存在 JetBrains 安全存储，不会自动共享给 Codex MCP。
3. 当前缺少“明确测试 source + 明确 schema + 只读凭据注入方式”；注册一个无法启动或可能连错库的 MCP 不构成权限修复。

确认具体测试库后，最小配置应只暴露一个明确 schema，并强制只读、限行：

```toml
# /Users/guozhixiang/.config/dbhub/opay-test.toml (chmod 600)
[[sources]]
id = "opay_test"
dsn = "${DBHUB_OPAY_TEST_DSN}"
lazy = true

[[tools]]
name = "execute_sql"
source = "opay_test"
readonly = true
max_rows = 200

[[tools]]
name = "search_objects"
source = "opay_test"
```

```toml
# 追加到 /Users/guozhixiang/.codex/config.toml
[mcp_servers.dbhub]
command = "npx"
args = ["-y", "@bytebase/dbhub@1.2.0", "--transport", "stdio", "--config", "/Users/guozhixiang/.config/dbhub/opay-test.toml"]
startup_timeout_sec = 120
```

推荐优先注册的最小范围应从代码链路中选择一项，而不是整台连接：

| 验证目标 | 推荐 source/schema |
|---|---|
| Okash 现金贷 Core | `ok 159.138.174.6` / `yinni_microloan` |
| EM 申请链路 | `em 159.138.165.0` / `okash` |
| ML 传统 Core | `ml 110.238.77.34` / `ml_microloan`（先解决 API/Batch 库名冲突） |
| ML OD | `ml 110.238.77.34` / `ml_bill` 或 `ml_od_trade` |
| BNPL | `bnpl 159.138.165.0` / `ep_bill`、`ep_pay_trade` 或 `ep_pay_channel`（每次一个） |

## 6. 证据索引与已知缺口

### 6.1 代码快照

| 仓库 | 当前分支 / commit |
|---|---|
| `EM/service-ng` | `master@f2419e5d8` |
| `CashLoan/core-ng` | `feature-20260827-trialCalcBatch@fb0ca4f` |
| `CashLoan/coupon-ng` | `master@2c0be2d` |
| `CashLoan/pay-channel-ng` | `master@f1e7ffb4` |
| `CashLoan/pay-trade` | `feature-20260805-manageFee@e5ac0e31` |
| `CashLoan/blieridge-into-gateway` | `feture-20260626-transferRepay@f5ecc70` |
| `ML/core` | `master@cad2110` |
| `ML/service-ng` | `master@28f35642f` |
| `ML/pay-trade` | `fix_releasePreDeductCoupon_faill@176272c` |
| `ML/pay-channel` | `master@26cbb38` |
| `ML/ml-reconciliation` | `master@3012308` |
| `ML/ml-coupon` | `master@4463f53` |
| `ML/ML_OD/ml-bill` | `feature-20260825-itemAccounting-V2@796a0f4` |
| `ML/ML_OD/ml-od-trade` | `master@a229303` |
| `BNPL/ease-pay-bill` | `feature-20260728-reconcile@43d1c4fc` |
| `BNPL/ease-pay-trade` | `feature_merchant_loan_balance_alert@62c860a` |
| `BNPL/ease-pay-channel` | `master@a37d5b8` |

### 6.2 关键证据路径

- DataGrip 连接：`/Users/guozhixiang/DataGripProjects/Opay/.idea/dataSources.xml`
- DataGrip scope：`/Users/guozhixiang/DataGripProjects/Opay/.idea/dataSources.local.xml`
- DataGrip 对象缓存：`/Users/guozhixiang/DataGripProjects/Opay/.idea/dataSources/*.xml`
- 现金贷/ML 代码盘点基线：`/Users/guozhixiang/Code/Loan System Docs/{CashLoan,ML}/09-数据库调查/`
- BNPL 代码盘点基线：`/Users/guozhixiang/Code/Loan System Docs/BNPL/09-数据库调查/`
- 当前代码直接证据：各仓 `src/main/**/Mapper.xml`、`@TableName` Entity、datasource 配置与仓内 SQL。
- DBS 在线页面：`https://dbs.opaymfb.com/sqlquery/`；2026-08-31 使用实例、数据库和 View Table Structure 下拉只读核验，未执行 SQL。
- DBS 已审批权限记录：`P-2268` ML Service、`P-2269` EM Service、`P-2270` EM Core、`P-2271` OK、`P-2272` ML Core、`P-2273` BNPL；有效期均显示至 2027-07-10。
- 用户提供的 5 张会话截图用于识别页面入口、查询历史和目标展示格式；附件临时路径不作为可长期复核证据，最终线上关系以本轮 DBS 实时下拉和权限记录为准。

### 6.3 DBS 完整实例清单（53/53）

状态口径：`已确认三元组` 表示正文已核验“实例 + 库 + 表/表族”；`实例-only` 只证明 DBS 下拉存在该实例，不能据名称、代码配置或相邻实例外推库表。环境和角色仅按实例原名归类，未额外推断部署用途。

#### 6.3.1 EM / OK（20）

| # | 引擎 | 归属 | DBS 实例（原文） | 状态 / 已确认库 |
|---:|---|---|---|---|
| 1 | MySQL | EM / Service | `10.215.25.89-ng-easemoni-service-slave` | `实例-only` |
| 2 | MySQL | EM / BI | `10.215.40.147-ng-easemoni-bi` | `实例-only` |
| 3 | MySQL | EM / Collection | `10.215.56.206-ng-easemoni-collection-slave` | `实例-only` |
| 4 | MySQL | EM / Core | `10.215.91.109-ng-easemoni-core-slave` | `实例-only`；代码配置另有库名线索，不计在线三元组 |
| 5 | MySQL | EM / Service | `10.215.99.85-ng-easemoni-service-master` | `实例-only`；展开时页面返回提示 |
| 6 | MySQL | OK / Service | `10.217.122.197-ng-okash-service-slave` | `实例-only` |
| 7 | MySQL | OK / BI | `10.217.2.80-ng-okash-bi` | `实例-only` |
| 8 | MySQL | OK / Core | `10.217.63.40-ng-okash-core-slave` | `实例-only`；代码配置另有库名线索，不计在线三元组 |
| 9 | MySQL | OK / Collection | `10.217.97.163-ng-okash-collection-slave` | `实例-only` |
| 10 | MySQL | EM / Test | `110.238.77.34-ng-easemoni-test` | `实例-only` |
| 11 | MySQL | OK / Test | `159.138.174.6-ng-okash-test` | `实例-only` |
| 12 | OceanBase | EM / Test | `10.220.4.60-ng_easemoni_test-ob` | `实例-only` |
| 13 | OceanBase | EM / White Service Test | `10.220.4.60-ng_loan_white_service_easemoni_test-ob` | `实例-only` |
| 14 | OceanBase | OK / White Service Test | `10.220.4.60-ng_loan_white_service_okash_test_ob` | `实例-only` |
| 15 | OceanBase | OK / Test | `10.220.4.60-ng_okash_test-ob` | `实例-only` |
| 16 | OceanBase | EM / Core | `10.224.10.17-ng-em-core-ob-slave` | `已确认三元组`：`microloan`, `microloan_coupon`, `microloan_admin` |
| 17 | OceanBase | EM / Message | `10.224.10.17-ng-em-message-ob-slave` | `实例-only`；不得自动关联 `message` 库 |
| 18 | OceanBase | EM / Service | `10.224.10.17-ng-em-service-ob-slave` | `已确认三元组`：`okash`, `pay_channel`, `tag_service`, `opay_finance_protocol`, `admin` |
| 19 | OceanBase | OK | `10.66.1.139-ng-okash-ob` | `已确认三元组`：`microloan`, `microloan_coupon`, `okash`, `pay_channel`, `message` |
| 20 | OceanBase | EM / White Service | `10.66.1.139-ng_loan_white_service_easemoni-ob` | `实例-only` |

#### 6.3.2 ML / Merchant Loan（6）

| # | 引擎 | 归属 | DBS 实例（原文） | 状态 / 已确认库 |
|---:|---|---|---|---|
| 21 | MySQL | ML / Service | `10.221.16.112-ng-ml-service-slave` | `实例-only` |
| 22 | MySQL | ML / BI | `10.221.16.20-ng-ml-bi-slave` | `实例-only` |
| 23 | MySQL | ML / BI | `10.222.6.101-ease-merchant-loan-bi-master` | `实例-only` |
| 24 | MySQL | ML / Core | `10.222.6.130-ease-merchant-loan-core-slave` | `已确认三元组`：`ml_microloan`, `ml_microloan_coupon`, `ml_bill` |
| 25 | MySQL | ML / Collection | `10.222.6.147-ease-merchant-loan-collection-slave` | `实例-only` |
| 26 | MySQL | ML / Service | `10.222.6.88-ease-merchant-loan-service-slave` | `已确认三元组`：`ml_ease`, `ml_admin`, `ml_protocol`, `ml_od_trade`, `ml_risk_gateway`, `okash_risk`, `ml_channel`, `ml_message`, `ml_gatekeeper` |

#### 6.3.3 BNPL / EP（17）

| # | 引擎 | 归属 | DBS 实例（原文） | 状态 / 已确认库 |
|---:|---|---|---|---|
| 27 | MySQL | BNPL / Prod | `10.226.10.216-ng-bnpl-prod-master` | `实例-only` |
| 28 | MySQL | BNPL / Prepare | `192.25.3.49-ng-bnpl-prepare-slave01` | `实例-only` |
| 29 | MySQL | BNPL / Prepare | `192.25.3.99-ng-bnpl-prepare-master` | `实例-only` |
| 30 | OceanBase | BNPL / Test | `10.220.4.60-ng_bnpl_test-ob` | `实例-only` |
| 31 | OceanBase | BNPL / Collect 汇总 | `10.226.10.147-ng_loan_collect-ob` | `已确认三元组`：`bnpl_ep_bill`, `bnpl_ep_pay_trade`, `bnpl_ep_pay_channel` |
| 32 | OceanBase | EP / Message | `10.226.10.3-ep-message-ob-slave` | `实例-only` |
| 33 | OceanBase | EP / Admin | `10.226.10.3-ep_admin-ob-slave` | `实例-only` |
| 34 | OceanBase | EP / Bill | `10.226.10.3-ep_bill-ob-slave` | `实例-only`；生产库表关系以已授权汇总实例为准 |
| 35 | OceanBase | EP / Collection | `10.226.10.3-ep_collection-ob-slave` | `实例-only` |
| 36 | OceanBase | EP / Consumer | `10.226.10.3-ep_consumer-ob-slave` | `实例-only` |
| 37 | OceanBase | EP / CRB | `10.226.10.3-ep_crb-ob-slave` | `实例-only` |
| 38 | OceanBase | EP / Protocol | `10.226.10.3-ep_protocol-ob-slave` | `实例-only` |
| 39 | OceanBase | EP / Quota Center | `10.226.10.3-ep_quota_center-ob-slave` | `实例-only` |
| 40 | OceanBase | EP / Risk Gateway | `10.226.10.3-ep_risk_gateway-ob-slave` | `实例-only` |
| 41 | OceanBase | EP / Service User | `10.226.10.3-ep_service_user-ob-slave` | `实例-only` |
| 42 | OceanBase | EP / Trade Channel | `10.226.10.3-ep_trade_channel-ob-slave` | `实例-only`；生产库表关系以已授权汇总实例为准 |
| 43 | OceanBase | EP / Marketing | `10.226.10.3-marketing_server-ob-slave` | `实例-only` |

#### 6.3.4 共享基础设施与非关系型排除项（10）

| # | 引擎 | 归属 | DBS 实例（原文） | 状态 / 范围说明 |
|---:|---|---|---|---|
| 44 | MySQL | Shared / Data Prepare | `10.66.0.151-ng-data-prepare-rds` | `实例-only`；未归入三条业务线 |
| 45 | MySQL | Shared / Nacos Apollo | `10.66.0.66-ng-base-nacosapollo-prod` | `实例-only`；配置基础设施，不外推业务表 |
| 46 | MySQL | Shared / DBA | `ng-base-dba-prod-10.66.1.247` | `实例-only`；未归入三条业务线 |
| 47 | OceanBase | Shared / Benefits Gateway | `10.66.0.80-ng-benefits_gateway-ob-master` | `实例-only`；当前 17 仓未形成已确认关系 |
| 48 | OceanBase | Shared / Unregister Gateway | `10.66.0.80-ng-loan_unregister_gateway-ob-master` | `实例-only`；当前 17 仓未形成已确认关系 |
| 49 | OceanBase | Shared / Task Center | `10.66.0.80-ng-task_center-ob-master` | `实例-only`；当前 17 仓未形成已确认关系 |
| 50 | Redis / DCS | EM / Tag Service | `ng-em-tag-service-dcs` | 非关系型排除项；不纳入“库 + 表”矩阵 |
| 51 | Redis | EM / Service | `redis-ng-easemoni-service-cluster` | 非关系型排除项；Redis key 不纳入关系表统计 |
| 52 | MongoDB / DDS | Shared / Risk Test | `159.138.170.11-ng-base-risk-dds-test` | 非关系型排除项；database/collection 不纳入本次关系表统计 |
| 53 | MongoDB / DDS | Shared / Risk Prod | `ng-base-risk-prod-10.66.1.10` | 非关系型排除项；database/collection 不纳入本次关系表统计 |

计数闭合：EM/OK 20 + ML 6 + BNPL/EP 17 + 共享/排除 10 = 53；按引擎为 MySQL 23 + OceanBase 26 + Redis 2 + MongoDB 2 = 53。关系型实例 49 个，非关系型排除项 4 个。

### 6.4 仍需运行库关闭的缺口

1. 本轮已绑定在线实例、库和表名，但没有执行 `SHOW CREATE TABLE` / `information_schema`，因此字段、索引、约束和部署 commit 仍未核验。
2. ML Core 线上已确认 `ml_microloan`；Batch/历史配置中的 `yinni_microloan` 仍是测试/历史候选，不应合并为同一生产库。
3. 多数仓库没有完整迁移历史；代码表存在不等于生产表结构一致。
4. BNPL 实时已命中 `funds_accounting_*` 与 `mq_exception_record`，但代码活动表 `bill_repay_trans_detail` 未在实时下拉出现，需要 DBA/部署链路继续确认。
5. 7 张 ML 仅模型候选与 4 张 EM 仅实体候选缺少活动调用链，不计为确认访问。
6. 未获库下拉的 MySQL/BI/Collection/Prepare 实例仅作为实例线索，不得外推到库表关系；本文件已与已确认三元组分表展示。
