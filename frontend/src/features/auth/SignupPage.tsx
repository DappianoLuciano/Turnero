import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Link, Navigate, useNavigate } from 'react-router';
import { z } from 'zod';
import { api, unwrap } from '@/shared/api/client';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/shared/ui/card';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import { login } from './auth-api';
import { useSession } from './use-session';

const v = t.auth.signup.validation;

const schema = z.object({
  businessName: z.string().trim().min(1, v.businessRequired).max(120, v.businessTooLong),
  fullName: z.string().trim().min(1, v.nameRequired).max(120, v.nameTooLong),
  email: z.string().trim().min(1, t.auth.validation.emailRequired).email(t.auth.validation.emailInvalid),
  password: z.string().min(8, v.passwordShort).max(128, v.passwordLong),
});

type FormValues = z.infer<typeof schema>;

/** Self-service registration: creates the business, starts the free trial and signs the owner in. */
export function SignupPage() {
  const session = useSession();
  const navigate = useNavigate();
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { businessName: '', fullName: '', email: '', password: '' },
  });
  const mutation = useMutation({
    mutationFn: async (values: FormValues) => {
      await unwrap(api.POST('/api/signup', { body: values }));
      await login({ email: values.email, password: values.password });
    },
    onSuccess: () => navigate('/app', { replace: true }),
  });

  if (session.status === 'authenticated' && !mutation.isPending && !mutation.isSuccess) {
    return <Navigate to="/app" replace />;
  }

  const { errors } = form.formState;
  const field = (name: keyof FormValues) => ({ id: `signup-${name}`, 'aria-invalid': !!errors[name] });

  return (
    <main className="bg-muted/40 flex min-h-full items-center justify-center p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <p className="text-muted-foreground text-sm font-semibold">{t.app.name}</p>
          <CardTitle>
            <h1 className="text-xl">{t.auth.signup.title}</h1>
          </CardTitle>
          <CardDescription>{t.auth.signup.subtitle}</CardDescription>
        </CardHeader>
        <CardContent>
          <form
            noValidate
            className="flex flex-col gap-4"
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            <div className="flex flex-col gap-2">
              <Label htmlFor="signup-businessName">{t.auth.signup.businessName}</Label>
              <Input
                placeholder={t.auth.signup.businessNamePlaceholder}
                {...field('businessName')}
                {...form.register('businessName')}
              />
              {errors.businessName && (
                <p className="text-destructive text-sm">{errors.businessName.message}</p>
              )}
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="signup-fullName">{t.auth.signup.fullName}</Label>
              <Input autoComplete="name" {...field('fullName')} {...form.register('fullName')} />
              {errors.fullName && <p className="text-destructive text-sm">{errors.fullName.message}</p>}
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="signup-email">{t.auth.login.email}</Label>
              <Input
                type="email"
                autoComplete="email"
                inputMode="email"
                placeholder={t.auth.login.emailPlaceholder}
                {...field('email')}
                {...form.register('email')}
              />
              {errors.email && <p className="text-destructive text-sm">{errors.email.message}</p>}
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="signup-password">{t.auth.login.password}</Label>
              <Input
                type="password"
                autoComplete="new-password"
                {...field('password')}
                {...form.register('password')}
              />
              <p className="text-muted-foreground text-xs">{t.auth.signup.passwordHint}</p>
              {errors.password && <p className="text-destructive text-sm">{errors.password.message}</p>}
            </div>
            {mutation.isError && (
              <p role="alert" className="text-destructive text-sm">
                {errorMessage(mutation.error)}
              </p>
            )}
            <Button type="submit" size="lg" disabled={mutation.isPending}>
              {mutation.isPending ? t.auth.signup.submitting : t.auth.signup.submit}
            </Button>
            <p className="text-muted-foreground text-center text-xs">{t.auth.signup.trialNote}</p>
            <p className="text-center text-sm">
              {t.auth.signup.haveAccount}{' '}
              <Link className="text-primary font-medium underline" to="/app/login">
                {t.auth.signup.goToLogin}
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </main>
  );
}
