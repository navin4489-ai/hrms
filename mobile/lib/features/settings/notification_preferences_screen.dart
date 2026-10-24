import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/me_models.dart';
import '../../providers/settings_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/form_error.dart';
import '../../widgets/section_card.dart';

/// Per-category notification channels (FR-EMP-121).
/// Mandatory categories cannot be disabled — the server rejects that with 422.
class NotificationPreferencesScreen extends ConsumerStatefulWidget {
  const NotificationPreferencesScreen({super.key});

  @override
  ConsumerState<NotificationPreferencesScreen> createState() =>
      _NotificationPreferencesScreenState();
}

class _NotificationPreferencesScreenState
    extends ConsumerState<NotificationPreferencesScreen>
    with ServerFieldErrors<NotificationPreferencesScreen> {
  /// Local editable copy, seeded from the first successful load.
  List<NotificationPreference>? _draft;
  bool _dirty = false;
  bool _saving = false;

  void _seed(List<NotificationPreference> items) {
    if (_draft != null) return;
    _draft = List<NotificationPreference>.of(items, growable: false);
  }

  void _update(
    int index,
    NotificationPreference Function(NotificationPreference) change,
  ) {
    setState(() {
      final next = List<NotificationPreference>.of(_draft!);
      next[index] = change(next[index]);
      _draft = next;
      _dirty = true;
    });
  }

  Future<void> _save() async {
    final draft = _draft;
    if (draft == null) return;
    setState(() {
      _saving = true;
      clearErrors();
    });
    try {
      await ref
          .read(settingsActionsProvider)
          .saveNotificationPreferences(draft);
      ref.invalidate(notificationPreferencesProvider);
      if (!mounted) return;
      setState(() => _dirty = false);
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text(Strings.preferencesSaved)));
    } on ApiException catch (e) {
      if (!mounted) return;
      final mapped = e.errors.isNotEmpty;
      captureError(e);
      if (!mapped) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              e.statusCode == 422
                  ? Strings.preferencesMandatoryError
                  : e.message,
            ),
          ),
        );
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text(Strings.offlineMessage)));
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final prefs = ref.watch(notificationPreferencesProvider);

    return AppScaffold(
      title: Strings.notificationPreferences,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(notificationPreferencesProvider),
        child: AsyncBody(
          value: prefs,
          onRetry: () => ref.invalidate(notificationPreferencesProvider),
          isEmpty: (items) => items.isEmpty,
          emptyTitle: Strings.noNotifications,
          emptyMessage: Strings.emptyGeneric,
          emptyIcon: Icons.tune_rounded,
          builder: (items) {
            _seed(items);
            final draft = _draft!;
            return ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.all(16),
              children: [
                if (bannerMessage != null)
                  FormErrorBanner(
                    message: bannerMessage!,
                    onDismiss: clearErrors,
                  ),
                Text(
                  Strings.mandatoryCategoryHint,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: Theme.of(context).colorScheme.outline),
                ),
                const SizedBox(height: 12),
                for (var i = 0; i < draft.length; i++)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: SectionCard(
                      title: AppFormatters.enumLabel(draft[i].category),
                      dense: true,
                      trailing: draft[i].mandatory
                          ? Tooltip(
                              message: Strings.mandatoryCategoryHint,
                              child: Icon(
                                Icons.lock_rounded,
                                size: 17,
                                color: Theme.of(context).colorScheme.outline,
                              ),
                            )
                          : null,
                      child: Column(
                        children: [
                          _ChannelRow(
                            label: Strings.channelInApp,
                            value: draft[i].inApp,
                            locked: draft[i].mandatory,
                            onChanged: (v) =>
                                _update(i, (p) => p.copyWith(inApp: v)),
                          ),
                          _ChannelRow(
                            label: Strings.channelPush,
                            value: draft[i].push,
                            locked: draft[i].mandatory,
                            onChanged: (v) =>
                                _update(i, (p) => p.copyWith(push: v)),
                          ),
                          _ChannelRow(
                            label: Strings.channelEmail,
                            value: draft[i].email,
                            locked: false,
                            onChanged: (v) =>
                                _update(i, (p) => p.copyWith(email: v)),
                          ),
                        ],
                      ),
                    ),
                  ),
                const SizedBox(height: 4),
                AppButton(
                  label: Strings.save,
                  icon: Icons.save_rounded,
                  busy: _saving,
                  onPressed: !_dirty || _saving ? null : _save,
                ),
                const SizedBox(height: 32),
              ],
            );
          },
        ),
      ),
    );
  }
}

/// One channel toggle with an accessible label.
class _ChannelRow extends StatelessWidget {
  const _ChannelRow({
    required this.label,
    required this.value,
    required this.onChanged,
    this.locked = false,
  });

  final String label;
  final bool value;
  final bool locked;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(child: Text(label)),
        if (locked)
          Icon(
            Icons.lock_rounded,
            size: 16,
            color: Theme.of(context).colorScheme.outline,
          ),
        Switch(value: value, onChanged: locked ? null : onChanged),
      ],
    );
  }
}
