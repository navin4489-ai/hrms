import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/validators.dart';
import '../../providers/profile_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/form_error.dart';

/// Edit non-sensitive profile fields (FR-EMP-021, `PUT /me/profile`).
/// Bank details / legal name / national ID go through the sensitive-change
/// workflow instead.
class EditProfileScreen extends ConsumerStatefulWidget {
  const EditProfileScreen({super.key});

  @override
  ConsumerState<EditProfileScreen> createState() => _EditProfileScreenState();
}

class _EditProfileScreenState extends ConsumerState<EditProfileScreen>
    with ServerFieldErrors<EditProfileScreen> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _preferredName;
  late final TextEditingController _phone;
  late final TextEditingController _personalEmail;
  late final TextEditingController _address;
  late final TextEditingController _emergencyName;
  late final TextEditingController _emergencyPhone;
  bool _busy = false;
  bool _prefilled = false;

  @override
  void initState() {
    super.initState();
    final me = ref.read(meProvider).value;
    _preferredName = TextEditingController(text: me?.preferredName ?? '');
    _phone = TextEditingController(text: me?.phone ?? '');
    _personalEmail = TextEditingController(text: me?.personalEmail ?? '');
    _address = TextEditingController(text: me?.address ?? '');
    _emergencyName = TextEditingController(
      text: me?.emergencyContactName ?? '',
    );
    _emergencyPhone = TextEditingController(
      text: me?.emergencyContactPhone ?? '',
    );
    _prefilled = me != null;
  }

  @override
  void dispose() {
    _preferredName.dispose();
    _phone.dispose();
    _personalEmail.dispose();
    _address.dispose();
    _emergencyName.dispose();
    _emergencyPhone.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      await ref
          .read(profileActionsProvider)
          .updateProfile(
            preferredName: _preferredName.text.trim(),
            phone: _phone.text.trim(),
            personalEmail: _personalEmail.text.trim(),
            address: _address.text.trim(),
            emergencyContactName: _emergencyName.text.trim(),
            emergencyContactPhone: _emergencyPhone.text.trim(),
          );
      ref.invalidate(meProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text(Strings.savedSuccessfully)));
      context.pop();
    } on ApiException catch (e) {
      captureError(e);
    } catch (_) {
      setState(() => bannerMessage = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AppScaffold(
      title: Strings.editProfile,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: Form(
            key: _formKey,
            autovalidateMode: AutovalidateMode.onUserInteraction,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (bannerMessage != null)
                  FormErrorBanner(
                    message: bannerMessage!,
                    onDismiss: clearErrors,
                  ),
                if (!_prefilled)
                  const Padding(
                    padding: EdgeInsets.only(bottom: 12),
                    child: LinearProgressIndicator(),
                  ),
                TextFormField(
                  controller: _preferredName,
                  enabled: !_busy,
                  decoration: InputDecoration(
                    labelText: Strings.preferredName,
                    prefixIcon: const Icon(Icons.person_outline_rounded),
                    errorText: errorFor('preferredName'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _phone,
                  enabled: !_busy,
                  keyboardType: TextInputType.phone,
                  validator: Validators.phoneIfPresent,
                  decoration: InputDecoration(
                    labelText: Strings.phone,
                    prefixIcon: const Icon(Icons.phone_outlined),
                    errorText: errorFor('phone'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _personalEmail,
                  enabled: !_busy,
                  keyboardType: TextInputType.emailAddress,
                  validator: Validators.emailIfPresent,
                  decoration: InputDecoration(
                    labelText: Strings.personalEmail,
                    prefixIcon: const Icon(Icons.mail_outline_rounded),
                    errorText: errorFor('personalEmail'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _address,
                  enabled: !_busy,
                  maxLines: 2,
                  decoration: InputDecoration(
                    labelText: Strings.address,
                    prefixIcon: const Icon(Icons.home_outlined),
                    errorText: errorFor('address'),
                  ),
                ),
                const SizedBox(height: 20),
                Text(
                  Strings.emergencyContact,
                  style: Theme.of(context).textTheme.titleSmall
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 10),
                TextFormField(
                  controller: _emergencyName,
                  enabled: !_busy,
                  decoration: InputDecoration(
                    labelText: Strings.emergencyContactName,
                    errorText: errorFor('emergencyContactName'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _emergencyPhone,
                  enabled: !_busy,
                  keyboardType: TextInputType.phone,
                  validator: Validators.phoneIfPresent,
                  decoration: InputDecoration(
                    labelText: Strings.emergencyContactPhone,
                    errorText: errorFor('emergencyContactPhone'),
                  ),
                ),
                const SizedBox(height: 14),
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: Theme.of(context).colorScheme.secondaryContainer
                        .withValues(alpha: 0.5),
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: Text(
                    Strings.sensitiveChangeHint,
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
                const SizedBox(height: 20),
                AppButton(
                  label: Strings.save,
                  busy: _busy,
                  onPressed: _busy ? null : _submit,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
