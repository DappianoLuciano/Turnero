import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { Link, Navigate, useLocation, useNavigate } from 'react-router';
import { z } from 'zod';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/shared/ui/card';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import { login } from './auth-api';
import { useSession } from './use-session';

const schema = z.object({
  email: z.string().trim().min(1, t.auth.validation.emailRequired).email(t.auth.validation.emailInvalid),
  password: z.string().min(1, t.auth.validation.passwordRequired),
});

type FormValues = z.infer<typeof schema>;

export function LoginPage() {
  const session = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? '/app';

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '' },
  });
  const mutation = useMutation({
    mutationFn: login,
    onSuccess: () => navigate(from, { replace: true }),
  });

  if (session.status === 'authenticated') {
    return <Navigate to={from} replace />;
  }

  const { errors } = form.formState;

  return (
    <main className="bg-muted/40 flex min-h-full items-center justify-center p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <p className="text-muted-foreground text-sm font-semibold">{t.app.name}</p>
          <CardTitle>
            <h1 className="text-xl">{t.auth.login.title}</h1>
          </CardTitle>
          <CardDescription>{t.auth.login.subtitle}</CardDescription>
        </CardHeader>
        <CardContent>
          <form
            noValidate
            className="flex flex-col gap-4"
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            <div className="flex flex-col gap-2">
              <Label htmlFor="email">{t.auth.login.email}</Label>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                inputMode="email"
                placeholder={t.auth.login.emailPlaceholder}
                aria-invalid={!!errors.email}
                {...form.register('email')}
              />
              {errors.email && <p className="text-destructive text-sm">{errors.email.message}</p>}
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="password">{t.auth.login.password}</Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                aria-invalid={!!errors.password}
                {...form.register('password')}
              />
              {errors.password && <p className="text-destructive text-sm">{errors.password.message}</p>}
            </div>
            {mutation.isError && (
              <p role="alert" className="text-destructive text-sm">
                {errorMessage(mutation.error)}
              </p>
            )}
            <Button type="submit" size="lg" disabled={mutation.isPending}>
              {mutation.isPending ? t.auth.login.submitting : t.auth.login.submit}
            </Button>
            <p className="text-center text-sm">
              {t.auth.noAccount}{' '}
              <Link className="text-primary font-medium underline" to="/app/signup">
                {t.auth.goToSignup}
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </main>
  );
}
