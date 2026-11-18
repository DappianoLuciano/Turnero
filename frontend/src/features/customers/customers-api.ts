import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

type CustomerDto = components['schemas']['CustomerResponse'];
type CustomerPageDto = components['schemas']['CustomerPage'];

export type Customer = CustomerDto;

/** Page with numeric fields (the generated types allow `number | string`). */
export interface CustomerPage {
  items: Customer[];
  total: number;
  page: number;
  pageSize: number;
}

export interface CustomerInput {
  name: string;
  phone: string | null;
  email: string | null;
  notes: string | null;
}

export interface CustomerFilters {
  search: string;
  includeArchived: boolean;
  page: number;
}

export const PAGE_SIZE = 25;

const customersKey = ['customers'] as const;

const toPage = (dto: CustomerPageDto): CustomerPage => ({
  items: dto.items,
  total: Number(dto.total),
  page: Number(dto.page),
  pageSize: Number(dto.pageSize),
});

export function useCustomers({ search, includeArchived, page }: CustomerFilters) {
  return useQuery({
    queryKey: [...customersKey, { search, includeArchived, page }],
    queryFn: async () =>
      toPage(
        await unwrap(
          api.GET('/api/customers', {
            params: { query: { search: search || undefined, includeArchived, page, pageSize: PAGE_SIZE } },
          }),
        ),
      ),
    placeholderData: keepPreviousData,
  });
}

export function useCreateCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CustomerInput) => unwrap(api.POST('/api/customers', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: customersKey }),
  });
}

export function useDeleteCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE('/api/customers/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: customersKey }),
  });
}

export function useUpdateCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: CustomerInput & { id: string; isArchived: boolean }) =>
      unwrap(api.PUT('/api/customers/{id}', { params: { path: { id } }, body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: customersKey }),
  });
}
